using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;
using System.Reflection;
using System;


class MemoryReaderWriter
{
    private Process process;

    public MemoryReaderWriter(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);

        if (processes.Length > 0)
        {
            process = processes[0];
        } else
        {
            Environment.Exit(0);
        }
    }

    public bool IsProcessRunning()
    {
        if (process != null)
        {
            process.Refresh();
            return !process.HasExited;
        }
        return false;
    }

    public byte[] ReadMemory(IntPtr address, int bytesToRead)
    {
        byte[] buffer = new byte[bytesToRead];
        IntPtr bytesRead;
        ReadProcessMemory(process.Handle, address, buffer, bytesToRead, out bytesRead);
        return buffer;
    }

    public int ReadMemoryAsInt(IntPtr address, int bytesToRead)
    {
        byte[] buffer = ReadMemory(address, bytesToRead);
        int value = BitConverter.ToInt32(buffer, 0);
        return value;
    }

    public void WriteMemory(IntPtr address, string hexValue)
    {
        byte[] value = HexStringToBytes(hexValue);
        IntPtr bytesWritten;
        WriteProcessMemory(process.Handle, address, value, value.Length, out bytesWritten);
    }

    public void WriteIntMemory(IntPtr address, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        IntPtr bytesWritten;
        WriteProcessMemory(process.Handle, address, bytes, bytes.Length, out bytesWritten);
    }

    private byte[] HexStringToBytes(string hex)
    {
        hex = Regex.Replace(hex, @"\s+", "");
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll")]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesWritten);
}


class Program
{
    static List<Dictionary<string, object>> ProcessXmlFiles(string adressXmlPath, string configXmlPath, string valeurLang)
    {
        List<Dictionary<string, object>> results = new List<Dictionary<string, object>>();

        XDocument adressXml = XDocument.Load(adressXmlPath);
        XDocument configXml = XDocument.Load(configXmlPath);

        foreach (var action in configXml.Descendants("action"))
        {
            var result = new Dictionary<string, object>();

            result["action_type"] = action.Element("action_type").Value;
            result["action_value"] = action.Element("action_value").Value;
            result["action_condition"] = action.Element("action_condition").Value;
            result["action_condition_folder"] = action.Element("action_condition_folder").Value;

            result["old_value"] = "-1";

            var conditionsList = new List<Dictionary<string, object>>();

            foreach (var condition in action.Descendants("condition"))
            {
                var conditionDict = new Dictionary<string, object>();

                conditionDict["type_condition"] = condition.Element("type_condition").Value;
                conditionDict["order_condition"] = condition.Element("order_condition").Value;
                conditionDict["value_condition"] = condition.Element("value_condition").Value;

                var adressElement = adressXml.Descendants("condition")
                    .FirstOrDefault(e => e.Element("name").Value == conditionDict["type_condition"].ToString());

                if (adressElement != null)
                {
                    conditionDict["bytes"] = adressElement.Element("bytes").Value;
                    conditionDict["value_hext"] = adressElement.Element(valeurLang).Value;
                }

                conditionsList.Add(conditionDict);
            }

            result["conditions"] = conditionsList;

            var adressActionType = adressXml.Descendants("condition")
                .FirstOrDefault(e => e.Element("name").Value == result["action_type"].ToString());
            if (adressActionType != null)
            {
                result["action_type_hext"] = adressActionType.Element(valeurLang).Value;
                result["action_type_bytes"] = adressActionType.Element("bytes").Value;
            }

            results.Add(result);
        }

        return results;
    }


    static void Main(string[] args)
    {
        MemoryReaderWriter memoryReaderWriter = new MemoryReaderWriter("ff7");

        if (!memoryReaderWriter.IsProcessRunning())
        {
            Environment.Exit(0);
        }

        string adressXmlPath = "adress.xml";
        string configXmlPath = "config.xml";
        string valeurLang;
        string executablePath = Assembly.GetEntryAssembly().Location;
        string executableDirectory = Path.GetDirectoryName(executablePath);
        string iniFilePath = Path.Combine(executableDirectory, "FF7_Multi_Trainer.ini");

        using (StreamReader reader = new StreamReader(iniFilePath))
        {
            valeurLang = ((char)reader.Read()).ToString();
        }

        var results = ProcessXmlFiles(adressXmlPath, configXmlPath, valeurLang);

    trainer_loop:

        if(!memoryReaderWriter.IsProcessRunning())
        {
            Environment.Exit(0);
        }

        foreach (var result in results)
        {
            int current_value = memoryReaderWriter.ReadMemoryAsInt((IntPtr)Convert.ToInt32((string)result["action_type_hext"],16), Convert.ToInt32((string)result["action_type_bytes"]));
            bool conditions_ok = true;
            int current_int;
            int current_int_condition;

           
            if (current_value != Convert.ToInt32(result["old_value"]))
            {
                result["old_value"] = current_value;

                foreach (var condition in result["conditions"] as List<Dictionary<string, object>>)
                {
                    current_int = memoryReaderWriter.ReadMemoryAsInt((IntPtr)Convert.ToInt32((string)condition["value_hext"], 16), Convert.ToInt32((string)condition["bytes"]));
                    current_int_condition = Convert.ToInt32(condition["value_condition"]);
                    
                    if ((string)condition["order_condition"] == "-") { if (current_int >= current_int_condition) { conditions_ok = false; break; } }
                    if ((string)condition["order_condition"] == "+") { if (current_int <= current_int_condition) { conditions_ok = false; break; } }
                    if ((string)condition["order_condition"] == "=") { if (current_int != current_int_condition) { conditions_ok = false; break; } }
                    if ((string)condition["order_condition"] == "!") { if (current_int == current_int_condition) { conditions_ok = false; break; } }
                }

                if (conditions_ok == true)
                {
                    if (result["action_condition"].ToString().Trim() == "change_value")
                    {
                        memoryReaderWriter.WriteIntMemory(new IntPtr(int.Parse((string)result["action_type_hext"], System.Globalization.NumberStyles.HexNumber)), Convert.ToInt32((string)result["action_value"]));
                    }

                    if (result["action_condition"].ToString().Trim() == "use_files")
                    {

                       DirectoryInfo executableDirectoryDI = new DirectoryInfo(Path.GetDirectoryName(executablePath));
                       DirectoryInfo sourceDirectoryDI = new DirectoryInfo(result["action_condition_folder"] + @"\" + result["action_value"]);

                        FolderCopyAll(sourceDirectoryDI, executableDirectoryDI);
                    }

                    
                }
            }
        }

        Thread.Sleep(25);
        goto trainer_loop;
        
    }


    public static void FolderCopyAll(DirectoryInfo source, DirectoryInfo target)
    {;

        foreach (FileInfo fi in source.GetFiles())
        {
            string destFileName = Path.Combine(target.FullName, fi.Name);
            File.Copy(fi.FullName, destFileName, true);
        }

        foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
        {
            DirectoryInfo nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
            FolderCopyAll(diSourceSubDir, nextTargetSubDir);
        }
    }

}
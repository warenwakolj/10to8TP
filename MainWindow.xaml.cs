using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.ComponentModel;
using Microsoft.Win32.TaskScheduler;
using System.Security.Policy;

namespace Win10to8
{
    public partial class MainWindow : Window
    {
        private int installedCount = 0;
        private BackgroundWorker worker;

        public MainWindow()
        {

            if (!IsRunningAsAdmin())
            {
                MessageBoxResult result = MessageBox.Show("This application must be run as an administrator. Relaunch with elevated privileges?",
                                                      "Administrator Privileges Required",
                                                      MessageBoxButton.YesNo,
                                                      MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    RelaunchAsAdmin();
                }
                else
                {
                    Application.Current.Shutdown();
                }
                return;
            }

            InitializeComponent();
            worker = new BackgroundWorker();
            worker.DoWork += Worker_DoWork;
            worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
        }


        private void RelaunchAsAdmin()
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                Process.Start(processInfo);
            }
            catch (Win32Exception)
            {

                MessageBox.Show("Administrator privileges are required to run this application.",
                                "Permission Denied",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }

            Application.Current.Shutdown();
        }


        private bool IsRunningAsAdmin()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }


        private void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("This will install all components and restart your PC when finished. Continue?",
                              "Confirmation",
                              MessageBoxButton.YesNo,
                              MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                btnInstall.IsEnabled = false;
                progressBar.Visibility = Visibility.Visible;
                worker.RunWorkerAsync();
                DownloadSymbols();
            }
        }
        private void UpdateStatus(string status)
        {

            Dispatcher.Invoke(() => statusText.Text = status);
        }



        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                UpdateStatus("Copying AWM files...");
                string awmSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "awm");
                string awmDestPath = Path.Combine("C:\\", "awm");

                if (Directory.Exists(awmSourcePath))
                {
                    CopyDirectory(awmSourcePath, awmDestPath);
                }
                else
                {
                    MessageBox.Show("AWM folder not found in Files directory!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                UpdateStatus("Copying theme files...");
                string themesSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "Themes");
                string themesDestPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Resources", "Themes");

                if (Directory.Exists(themesSourcePath))
                {
                    CopyDirectory(themesSourcePath, themesDestPath);
                }
                else
                {
                    MessageBox.Show("Themes folder not found in Files directory!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                UpdateStatus("Installing Windhawk...");
                string installersPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "Installers");
                RunInstaller(
                    Path.Combine(installersPath, "windhawk_setup_offline.exe"),
                    "/S /D C:/10to8/Windhawk"
                );

                UpdateStatus("Copying Windhawk files...");
                string windhawkSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "Windhawk");
                string windhawkDestPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Windhawk");

                if (Directory.Exists(windhawkSourcePath))
                {
                    CopyDirectory(windhawkSourcePath, windhawkDestPath);
                }
                else
                {
                    MessageBox.Show("Windhawk folder not found in Files directory!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                UpdateStatus("Downloading symbols");
                DownloadSymbols("C:\\Windows\\System32\\ExplorerFrame.dll");
                DownloadSymbols("C:\\Windows\\explorer.exe");
                DownloadSymbols("C:\\Windows\\System32\\dwmapi.dll");
                DownloadSymbols("C:\\Windows\\System32\\dwmcore.dll");
                DownloadSymbols("C:\\Windows\\System32\\uDWM.dll");
                DownloadSymbols("C:\\Windows\\System32\\winlogon.exe");
                DownloadSymbols("C:\\Windows\\System32\\uxtheme.dll");
                DownloadSymbols("C:\\Windows\\AltTab.dll");

                UpdateStatus("Installing StartIsBack++...");
                RunInstaller(
                    Path.Combine(installersPath, "StartIsBackPlusPlus_setup.exe"),
                    "/elevated /silent"
                );


                UpdateStatus("Registering DLL...");
                RegisterDll();

                UpdateStatus("Importing scheduled tasks...");
                ImportScheduledTasks();

                UpdateStatus("Importing registry entries...");
                ImportRegistryEntries();

                UpdateStatus("Installation complete!");
            }
            catch (Exception ex)
            {
                UpdateStatus("Installation failed!");
                MessageBox.Show($"Error during installation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }   

        private void ImportScheduledTasks()
        {
            string tasksPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "Tasks");
            string[] taskFiles = {
              
                "StartAWM.xml",
                "WindhawkRunUITask.xml",
                "WindhawkUpdateTask.xml"
            };

            foreach (string taskFile in taskFiles)
            {
                string fullPath = Path.Combine(tasksPath, taskFile);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = "schtasks",
                            Arguments = $"/create /tn \"{Path.GetFileNameWithoutExtension(taskFile)}\" /xml \"{fullPath}\" /f",
                            UseShellExecute = true,
                            Verb = "runas",
                            CreateNoWindow = true
                        };

                        using (Process process = Process.Start(startInfo))
                        {
                            process.WaitForExit();
                            if (process.ExitCode != 0)
                            {
                                MessageBox.Show($"Error importing task: {taskFile}", "Task Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error importing task {taskFile}: {ex.Message}", "Task Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show($"Task file not found: {taskFile}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        private bool ImportRegistryFile(string regFile)
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "Registry", regFile);
            if (!File.Exists(fullPath))
            {
                MessageBox.Show($"Registry file not found: {regFile}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = $"import \"{fullPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        using (Process retryProcess = Process.Start(startInfo))
                        {
                            retryProcess.WaitForExit();
                            if (retryProcess.ExitCode != 0)
                            {
                                MessageBox.Show($"Error importing registry file: {regFile}", "Registry Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return false;
                            }
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing registry file {regFile}: {ex.Message}", "Registry Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }


        private void ImportRegistryEntries()
        {
            string[] regFiles = {
        "Windowmetrics.reg",
        "EnableOldAudio.reg",
        "DisableActionCenter.reg",
        "AWM.reg",
        "Windhawk.reg"  
    };

            foreach (string regFile in regFiles)
            {
                if (!ImportRegistryFile(regFile))
                {
         
                }
            }
        }

        private void RestartComputer()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /t 0",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initiating restart: {ex.Message}", "Restart Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RegisterDll()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "regsvr32",
                    Arguments = "c:\\awm\\msdia140_awm.dll",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        MessageBox.Show("Error registering msdia140_awm.dll", "Registration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error registering DLL: {ex.Message}", "Registration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                string destFilePath = Path.Combine(destinationDir, fileName);
                try
                {
                    File.Copy(filePath, destFilePath, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error copying file {fileName}: {ex.Message}", "Copy Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            foreach (string subdirPath in Directory.GetDirectories(sourceDir))
            {
                string folderName = Path.GetFileName(subdirPath);
                string destSubDir = Path.Combine(destinationDir, folderName);
                try
                {
                    CopyDirectory(subdirPath, destSubDir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error copying folder {folderName}: {ex.Message}", "Copy Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// this is my first cs function, don't kill me with hammers
        private void DownloadSymbols(string target)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "Tools", "pdblister.exe"),
                Arguments = "download_single SRV*C:\\ProgramData\\Windhawk\\Engine\\Symbols*https://msdl.microsoft.com/download/symbols target",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };
        }
        
        private void RunInstaller(string path, string arguments)
        {
            if (!File.Exists(path))
            {
                MessageBox.Show($"Installer not found: {path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        installedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing {Path.GetFileName(path)}: {ex.Message}",
                              "Installation Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            }
        }

        private void DownloadSymbols()
        {
            string symchkPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "Tools", "symchk.exe");
            string symbolsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Windhawk", "Engine", "Symbols");
            string serverUrl = "https://msdl.microsoft.com/download/symbols";

            Directory.CreateDirectory(symbolsPath);

            string[] filesToProcess = {
        @"C:\Windows\System32\ExplorerFrame.dll",
        @"C:\Windows\explorer.exe",
        @"C:\Windows\System32\dwmapi.dll",
        @"C:\Windows\System32\dwmcore.dll",
        @"C:\Windows\System32\uDWM.dll",
        @"C:\Windows\System32\LogonUI.exe",
        @"C:\Windows\System32\winlogon.exe",
        @"C:\Windows\System32\rundll32.exe",
        @"C:\Windows\System32\uxtheme.dll",
        @"C:\Windows\System32\svchost.exe",
        @"C:\Windows\AltTab.dll",
        @"C:\Windows\ImmersiveControlPanel\systemsettings.exe",

    };

            foreach (var filePath in filesToProcess)
            {
                if (File.Exists(filePath))
                {
                    try
                    {
                        string arguments = $"{filePath} /s srv*{symbolsPath}*{serverUrl}";
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = symchkPath,
                            Arguments = arguments,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        using (Process process = Process.Start(startInfo))
                        {
                            process.WaitForExit();

                            string output = process.StandardOutput.ReadToEnd();
                            string error = process.StandardError.ReadToEnd();

                            if (process.ExitCode != 0)
                            {
                                MessageBox.Show($"Error downloading symbols for {Path.GetFileName(filePath)}:\n{error}",
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Exception while processing {filePath}: {ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show($"File not found: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            MessageBox.Show("Symbol download process completed.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }


        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            progressBar.Visibility = Visibility.Collapsed;

            if (installedCount == 2)
            {
                MessageBox.Show("Installation completed successfully! The computer will restart.",
                              "Success",
                              MessageBoxButton.OK,
                              MessageBoxImage.Information);
                RestartComputer();
            }
            else
            {
                if (MessageBox.Show($"Installation completed with errors. {installedCount}/2 applications installed successfully.\n\nDo you still want to restart the computer?",
                                  "Warning",
                                  MessageBoxButton.YesNo,
                                  MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    RestartComputer();
                }
            }

            Close();
        }
    }
}
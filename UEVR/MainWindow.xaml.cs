using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Diagnostics;
using System.Security.Policy;
using System.Windows.Threading;
using System.Reflection;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows.Markup;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;

using Microsoft.Extensions.Configuration.Ini;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using static UEVR.SharedMemory;
using System.Threading.Channels;
using System.Security.Principal;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Path = System.IO.Path;
using System.Runtime.Serialization;
using System.DirectoryServices;
using System.Security.Cryptography;
using static System.Net.Mime.MediaTypeNames;
using System.Security.AccessControl;
using static UEVR.Injector;


namespace UEVR {
    class GameSettingEntry : INotifyPropertyChanged {
        private string _key = "";
        private string _value = "";
        private string _tooltip = "";
     
        public string Key { get => _key; set => SetProperty(ref _key, value); }
        public string Value { 
            get => _value; 
            set { 
                SetProperty(ref _value, value); 
                OnPropertyChanged(nameof(ValueAsBool)); 
            } 
        }

        public string Tooltip { get => _tooltip; set => SetProperty(ref _tooltip, value); }

        public int KeyAsInt { get { return Int32.Parse(Key); } set { Key = value.ToString(); } }
        public bool ValueAsBool { 
            get => Boolean.Parse(Value);
            set { 
                Value = value.ToString().ToLower();
            } 
        }

    

        public Dictionary<string, string> ComboValues { get; set; } = new Dictionary<string, string>();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null) {
            if (Equals(storage, value)) return false;
            if (propertyName == null) return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    };

    enum RenderingMethod {
        [Description("Native Stereo")]
        NativeStereo = 0,
        [Description("Synced Sequential")]
        SyncedSequential = 1,
        [Description("Alternating/AFR")]
        Alternating = 2
    };

    enum SyncedSequentialMethods {
        SkipTick = 0,
        SkipDraw = 1,
    };

    class ComboMapping {

        public static Dictionary<string, string> RenderingMethodValues = new Dictionary<string, string>(){
            {"0", "Native Stereo" },
            {"1", "Synced Sequential" },
            {"2", "Alternating/AFR" }
        };

        public static Dictionary<string, string> SyncedSequentialMethodValues = new Dictionary<string, string>(){
            {"0", "Skip Tick" },
            {"1", "Skip Draw" },
        };

        public static Dictionary<string, Dictionary<string, string>> KeyEnums = new Dictionary<string, Dictionary<string, string>>() {
            { "VR_RenderingMethod", RenderingMethodValues },
            { "VR_SyncedSequentialMethod", SyncedSequentialMethodValues },
        };
    };

    class MandatoryConfig {
        public static Dictionary<string, string> Entries = new Dictionary<string, string>() {
            { "VR_RenderingMethod", ((int)RenderingMethod.NativeStereo).ToString() },
            { "VR_SyncedSequentialMethod", ((int)SyncedSequentialMethods.SkipDraw).ToString() },
            { "VR_UncapFramerate", "true" },
            { "VR_Compatibility_SkipPostInitProperties", "false" }
        };
    };

    class GameSettingTooltips {
        public static string VR_RenderingMethod =
        "Native Stereo: The default, most performant, and best looking rendering method (when it works). Runs through the native UE stereo pipeline. Can cause rendering bugs or crashes on some games.\n" +
        "Synced Sequential: A form of AFR. Can fix many rendering bugs. It is fully synchronized with none of the usual AFR artifacts. Causes TAA/temporal effect ghosting.\n" +
        "Alternating/AFR: The most basic form of AFR with all of the usual desync/artifacts. Should generally not be used unless the other two are causing issues.";

        public static string VR_SyncedSequentialMethod =
        "Requires \"Synced Sequential\" rendering to be enabled.\n" +
        "Skip Tick: Skips the engine tick on the next frame. Usually works well but sometimes causes issues.\n" +
        "Skip Draw: Skips the viewport draw on the next frame. Works with least issues but particle effects can play slower in some cases.\n";

        public static Dictionary<string, string> Entries = new Dictionary<string, string>() {
            { "VR_RenderingMethod", VR_RenderingMethod },
            { "VR_SyncedSequentialMethod", VR_SyncedSequentialMethod },
        };
    }

    public class ValueTemplateSelector : DataTemplateSelector {
        public DataTemplate? ComboBoxTemplate { get; set; }
        public DataTemplate? TextBoxTemplate { get; set; }
        public DataTemplate? CheckboxTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container) {
            var keyValuePair = (GameSettingEntry)item;
            if (ComboMapping.KeyEnums.ContainsKey(keyValuePair.Key)) {
                return ComboBoxTemplate;
            } else if (keyValuePair.Value.ToLower().Contains("true") || keyValuePair.Value.ToLower().Contains("false")) {
                return CheckboxTemplate;
            } else {
                return TextBoxTemplate;
            }
        }
    }

    enum InjectableProcCode
        {
        Valid,
        Exclude,
        Warn
        }

    public partial class MainWindow : Window {
        // variables
        // process list
        private List<Process> m_processList = new List<Process>();
        private MainWindowSettings m_mainWindowSettings = new MainWindowSettings();

        private string m_lastSelectedProcessName = new string("");
        private int m_lastSelectedProcessId = 0;
       
        private SharedMemory.Data? m_lastSharedData = null;
        private bool m_connected = false;

        private DispatcherTimer m_updateTimer = new DispatcherTimer {
            Interval = new TimeSpan(0, 0, 1)
        };

        private IConfiguration? m_currentConfig = null;
        private string? m_currentConfigPath = null;
        private ExecutableFilter m_executableFilter = new ExecutableFilter();
        private string? m_commandLineAttachExe = null;
        private bool m_ignoreFutureVDWarnings = false;

        private int m_pid = 0;
        private string m_launchTarget = "";
        private string m_LaunchModeArgs = "";
        private bool m_launchMode = false;
        private bool m_startSuspended = true;
        //simply leaves it suspended
        public bool m_waitForDebugger = false;



        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        public MainWindow() {
            InitializeComponent();
            
            // Grab the command-line arguments
            string[] args = Environment.GetCommandLineArgs();

            // Parse and handle arguments
            foreach (string arg in args) {
                if (arg.EndsWith(".exe")) {
                    m_commandLineAttachExe = arg.Split('=')[1];
                    if (arg.StartsWith("--attach=")) {
                        m_launchMode = false;
                    }
                    //allow directly setting launch mode from arg, leaving old behavior on --attach for other launchers to call 
                    //I guess for consistency I should refactor to using an actual window setting
                    else if (arg.StartsWith("--launch")){
                        m_launchMode = true;
                        LaunchModeEnabledImpl ( );                        
                        if ( arg.Contains ( "=" ) )
                            {
                            m_launchTarget = arg.Split ( '=' ) [ 1 ];
                            if ( args.Length > 1 )
                                {
                                foreach ( var _arg in args.Skip ( 1 ) )
                                    m_LaunchModeArgs += " "+_arg;
                                }
                            InitializeConfig ( Path.GetFileNameWithoutExtension ( m_launchTarget ) );
                            Launch_Clicked_Impl ( );
                            }                      
                    }
                }
            }
        }

        public static bool IsAdministrator() {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            if (!IsAdministrator()) {
                m_nNotificationsGroupBox.Visibility = Visibility.Visible;
                m_restartAsAdminButton.Visibility = Visibility.Visible;
                m_adminExplanation.Visibility = Visibility.Visible;
            }

            if ( !Directory.Exists ( Path.Combine ( GetGlobalDir ( ), "excluded" ) ) )
                CleanupExclusions.Cleanup ( );

            if ( !m_launchMode) {
                 FillProcessList();
            }
            m_openvrRadio.IsChecked = m_mainWindowSettings.OpenVRRadio;
            m_openxrRadio.IsChecked = m_mainWindowSettings.OpenXRRadio;
            m_nullifyVRPluginsCheckbox.IsChecked = m_mainWindowSettings.NullifyVRPluginsCheckbox;
            m_ignoreFutureVDWarnings = m_mainWindowSettings.IgnoreFutureVDWarnings;
            m_focusGameOnInjectionCheckbox.IsChecked = m_mainWindowSettings.FocusGameOnInjection;

            m_updateTimer.Tick += (sender, e) => Dispatcher.Invoke(MainWindow_Update);
            m_updateTimer.Start();
        }

        private static bool IsExecutableRunning(string executableName ) {
            return Process.GetProcesses().Any(p => p.ProcessName.Equals(executableName, StringComparison.OrdinalIgnoreCase));
        }

        //I don't love this handling but I was starting write myself into a hole. This seems okay. 
        private void ExcludeProcess ( string procName )
            {
            CleanupExclusions.Cleanup ( );
            List<string> excludedProcesses = SyncExclusions ( new List<string> ());
            // allow for user override in case some games are failing and getting blocked 
            if ( File.Exists ( "include.txt" ) )
                {
                var lines = File.ReadAllLines ( "include.txt" ).ToList ( );

                if ( lines.Contains ( procName ) )
                    {
                    if ( excludedProcesses.Contains ( procName ) )
                        {
                        excludedProcesses.Remove ( procName );
                        }
                    }
                }
                if ( !excludedProcesses.Contains ( procName ) ) excludedProcesses.Add ( procName );
                else
                    {
                    if ( procName.EndsWith ( "Win64-Shipping" ) ) excludedProcesses.Remove ( procName );
                    if ( Directory.Exists ( Path.Combine ( GetGlobalDir ( ), "excluded", procName ) ) ) return;
                    else if ( Directory.Exists ( Path.Combine ( GetGlobalDir ( ), procName ) ) ) excludedProcesses.Remove ( procName );
                    if ( Path.GetFileNameWithoutExtension ( m_launchTarget ) == procName ) excludedProcesses.Remove ( procName );
                    }
            SyncExclusions ( excludedProcesses );
            }

        ////only logic happening here is duplicate handling
        //private List<string> SyncExclusions ( )
        //    {
        //    List<string> excludedProcesses = new List<string> ( );
        //    using ( FileStream fs = new FileStream ( "excluded.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read ) )
        //    using ( StreamReader sr = new StreamReader ( fs ) )
        //    using ( StreamWriter sw = new StreamWriter ( fs ) )
        //        {
        //        fs.Position = 0;
        //        if ( excludedProcesses.Count == 0 ) {
        //                foreach ( var line in sr.ReadToEnd ( ).Split ( new string [ ] { "\r\n" }, StringSplitOptions.None ).ToList ( ) )
        //                    {
        //                    if ( !excludedProcesses.Contains ( line ) ) excludedProcesses.Add ( line );
        //                    }     
        //            }
        //        else {                  
        //                sw.Write ( string.Join ( "\r\n", excludedProcesses ) );
        //            }
        //        fs.SetLength ( fs.Position );   
        //            }
        //    return excludedProcesses;
        //        }

        private List<string> SyncExclusions ( List<string> excludedProcesses )
            {
            using ( FileStream fs = new FileStream ( "excluded.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read ) )
            using ( StreamReader sr = new StreamReader ( fs ) )
            using ( StreamWriter sw = new StreamWriter ( fs ) )
                {
                fs.Position = 0;
                if ( excludedProcesses.Count == 0 )
                    {
                    foreach ( var line in sr.ReadToEnd ( ).Split ( new string [ ] { "\r\n" }, StringSplitOptions.None ).ToList ( ) )
                        {
                        if ( !excludedProcesses.Contains ( line ) ) excludedProcesses.Add ( line );
                        }
                    }
                else
                    {
                    sw.Write ( string.Join ( "\r\n", excludedProcesses ) );
                    }
                fs.SetLength ( fs.Position );
                }
            return excludedProcesses;
            }

        private void RestartAsAdminButton_Click ( object sender, RoutedEventArgs e ) {
            RestartAsAdminButton_Impl ( null );
            }

        //for direct calling
        private void RestartAsAdminButton_Impl (bool? launch )
            {

            // Get the path of the current executable
            var mainModule = Process.GetCurrentProcess ( ).MainModule;
            if ( mainModule == null )
                {
                return;
                }

            var exePath = mainModule.FileName;
            if ( exePath == null )
                {
                return;
                }

            // Create a new process with administrator privileges
            var processInfo = new ProcessStartInfo
                {
                FileName = exePath,
                Verb = "runas",
                UseShellExecute = true,
                Arguments = launch == true ? " --launch " : ""
                };

            try
                {
                // Attempt to start the process
                Process.Start ( processInfo );
                }
            catch ( Win32Exception ex )
                {
                // Handle the case when the user cancels the UAC prompt or there's an error
                MessageBox.Show ( $"Error: {ex.Message}\n\nThe application will continue running without administrator privileges.", "Failed to Restart as Admin", MessageBoxButton.OK, MessageBoxImage.Warning );
                return;
                }

            // Close the current application instance
            System.Windows.Application.Current.Shutdown ( );
            }

        private DateTime m_lastAutoInjectTime = DateTime.MinValue;
        
        private void Update_InjectStatus() {
            if (m_connected) {
                m_injectButton.Content = "Terminate Connected Process";
                return;
            }

            DateTime now = DateTime.Now;
            TimeSpan oneSecond = TimeSpan.FromSeconds(1);

            if (m_commandLineAttachExe == null) {
                if (m_lastSelectedProcessId == 0) {
                    m_injectButton.Content = "Inject";
                    return;
                }

                try {
                    var verifyProcess = Process.GetProcessById(m_lastSelectedProcessId);

                    if (verifyProcess == null || verifyProcess.HasExited || verifyProcess.ProcessName != m_lastSelectedProcessName) {
                        var processes = Process.GetProcessesByName(m_lastSelectedProcessName);

                        if (processes == null || processes.Length == 0 || !AnyInjectableProcesses(processes)) {
                            m_injectButton.Content = "Waiting for Process";
                            return;
                        }
                    }

                    m_injectButton.Content = "Inject";              }
                catch (ArgumentException) {
                    var processes = Process.GetProcessesByName(m_lastSelectedProcessName);

                    if (processes == null || processes.Length == 0 || !AnyInjectableProcesses(processes)) {
                        m_injectButton.Content = "Waiting for Process";
                        return;
                    }

                    m_injectButton.Content = "Inject";
                }
            } else {
                m_injectButton.Content = "Waiting for " + m_commandLineAttachExe.ToLower() + "...";

                var processes = Process.GetProcessesByName(m_commandLineAttachExe.ToLower().Replace(".exe", ""));

                if (processes.Count() == 0) {
                    return;
                }

                Process? process = null;

                foreach (Process p in processes) {
                    if (IsInjectableProcess(p) == InjectableProcCode.Valid) {
                        m_lastSelectedProcessId = p.Id;
                        m_lastSelectedProcessName = p.ProcessName;
                        process = p;
                    }
                    else
                        {
                            
                        }
                }

                if (process == null) {
                    return;
                }

                if (now - m_lastAutoInjectTime > oneSecond) {
                    if (m_nullifyVRPluginsCheckbox.IsChecked == true) {
                        IntPtr nullifierBase;
                        if (Injector.InjectDll(process.Id, "UEVRPluginNullifier.dll", out nullifierBase) && nullifierBase.ToInt64() > 0) {
                            if (!Injector.CallFunctionNoArgs(process.Id, "UEVRPluginNullifier.dll", nullifierBase, "nullify", true)) {
                                MessageBox.Show("Failed to nullify VR plugins.");
                            }
                        } else {
                            MessageBox.Show("Failed to inject plugin nullifier.");
                        }
                    }

                    string runtimeName;

                    if (m_openvrRadio.IsChecked == true) {
                        runtimeName = "openvr_api.dll";
                    } else if (m_openxrRadio.IsChecked == true) {
                        runtimeName = "openxr_loader.dll";
                    } else {
                        runtimeName = "openvr_api.dll";
                    }

                    if (Injector.InjectDll(process.Id, runtimeName)) {
                        InitializeConfig(process.ProcessName);

                        try {
                            if (m_currentConfig != null) {
                                if (m_currentConfig["Frontend_RequestedRuntime"] != runtimeName) {
                                    m_currentConfig["Frontend_RequestedRuntime"] = runtimeName;
                                    RefreshConfigUI();
                                    SaveCurrentConfig();
                                }
                            }
                        } catch (Exception) {

                        }

                        Injector.InjectDll(process.Id, "UEVRBackend.dll");
                    }

                    m_lastAutoInjectTime = now;
                   m_commandLineAttachExe = null;
                        FillProcessList();
                    if (m_focusGameOnInjectionCheckbox.IsChecked == true)
                    {
                        SwitchToThisWindow(process.MainWindowHandle, true);
                    }
                }
            }
        }

        private void Hide_ConnectionOptions() {
            m_openGameDirectoryBtn.Visibility = Visibility.Collapsed;
        }

        private void Show_ConnectionOptions() {
            m_openGameDirectoryBtn.Visibility = Visibility.Visible;
        }



        private DateTime lastInjectorStatusUpdate = DateTime.MinValue;
        private DateTime lastFrontendSignal = DateTime.MinValue;
        private DateTime m_lastRespondingTime = DateTime.MinValue;
        private void Update_InjectorConnectionStatus() {
            var data = SharedMemory.GetData();
            DateTime now = DateTime.Now;
            TimeSpan oneSecond = TimeSpan.FromSeconds(1);
            if(m_pid != 0 )
                {
                if (ClosedOrUnresponsive( Process.GetProcessById ( m_pid )) &&
                    m_ExperimentalSettingsCheckbox.IsChecked == true)
                    {
                    m_pid = 0;
                    if ( data != null ) SharedMemory.SendCommand ( SharedMemory.Command.Quit );
                    CleanLocalRuntime ( );
                    m_connected = false;
                    }
                }
            if (data != null) {

                //this is a little silly and I've probably made more work for myself by not simply using shmem for both but I think I'm using enough references to the pid it makes sense to have as a member var rather than calling getdata anytime I need it
                if ( m_launchMode && m_pid == 0 ) m_pid = (int)data?.pid;
                m_connectionStatus.Text = UEVRConnectionStatus.Connected;
                m_connectionStatus.Height = 51;
                m_connectionStatus.Text += ": " + data?.path;
                m_connectionStatus.Text += "\nThread ID: " + data?.mainThreadId.ToString ( );
                m_connectionStatus.Text += "     Process ID: ";
                m_connectionStatus.Text += m_launchMode ? $"{m_pid}" : $"{data?.pid}";
                Process p = Process.GetProcessById ( ( int ) data?.pid );
                m_connectionStatus.Text += "    Start Time: " + p.StartTime.ToString ( );
                m_connectionStatus.Text += "\nWorking Set: " + (p.WorkingSet64 / ( 1024 * 1024 )).ToString() + " MB";
                m_lastSharedData = data;
                m_connected = true;
                Show_ConnectionOptions();
                if ( p.Responding ) m_lastRespondingTime = now;
                if (data?.signalFrontendConfigSetup == true && (now - lastFrontendSignal > oneSecond)) {
                    SharedMemory.SendCommand(SharedMemory.Command.ConfigSetupAcknowledged);
                    RefreshCurrentConfig();

                    lastFrontendSignal = now;
                }
            } else {

                if (m_connected && !string.IsNullOrEmpty(m_commandLineAttachExe) && !m_launchMode) 
                {
                    // If we launched with an attached game exe, we shut ourselves down once that game closes unless using the frontend as a launcher
                    System.Windows.Application.Current.Shutdown();
                    return;
                }
                m_connectionStatus.Height = 21;
                m_connectionStatus.Text = UEVRConnectionStatus.NoInstanceDetected;
                m_connected = false;
                Hide_ConnectionOptions();
            }

            lastInjectorStatusUpdate = now;
        }

        private bool ClosedOrUnresponsive(Process p )
            {
            TimeSpan sixtySeconds = TimeSpan.FromSeconds ( 60 );

            if ( p.HasExited ) return true;
                    DateTime now = DateTime.Now;
            if ( !p.Responding && now - m_lastRespondingTime > sixtySeconds ) return true;
            return false;
            }

        private string GetGlobalDir() {
            string directory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            directory += "\\UnrealVRMod";

            if (!System.IO.Directory.Exists(directory)) {
                System.IO.Directory.CreateDirectory(directory);
            }

            return directory;
        }

        private string GetGlobalGameDir(string gameName) {
            string directory = GetGlobalDir() + "\\" + gameName;

            if (!System.IO.Directory.Exists(directory)) {
                System.IO.Directory.CreateDirectory(directory);
            }

            return directory;
        }

        private void NavigateToDirectory(string directory) {
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string explorerPath = System.IO.Path.Combine(windowsDirectory, "explorer.exe");
            Process.Start(explorerPath, "\"" + directory + "\"");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Left) 
                this.DragMove();
            }
        

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }

        private string GetGlobalDirPath() {
            string directory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            directory += "\\UnrealVRMod";
            return directory;
        }

        private void OpenGlobalDir_Clicked(object sender, RoutedEventArgs e) {
            string directory = GetGlobalDirPath();

            if (!System.IO.Directory.Exists(directory)) {
                System.IO.Directory.CreateDirectory(directory);
            }

            NavigateToDirectory(directory);
        }

        private void OpenGameDir_Clicked(object sender, RoutedEventArgs e) {
            if (m_lastSharedData == null) {
                return;
            }

            var directory = System.IO.Path.GetDirectoryName(m_lastSharedData?.path);
            if (directory == null) {
                return;
            }

            NavigateToDirectory(directory);
        }

        private void OpenGameDir_PreviewDragOver ( object sender, DragEventArgs e )
            {
            e.Handled = m_ExperimentalSettingsCheckbox.IsChecked == true;
            }

        //experimental switch allows drag and drop files to game dir e.g. for manual loading wrapper dlls or runtimes for reinit
        //could track what we've dropped there and cleanup automatically but lets be honest this feature is probably the least likely to be used or accepted for merge
        private void OpenGameDir_Drop ( object sender, DragEventArgs e )
            {
            if ( m_lastSharedData == null )
                {
                return;
                }
            if ( e.Data.GetDataPresent ( DataFormats.FileDrop ) )
                {
                string [ ] files = ( string [ ] ) e.Data.GetData ( DataFormats.FileDrop );
                var file = files [ 0 ];
                try
                    {                
                    File.Copy ( file, Path.Combine ( Path.GetDirectoryName ( m_lastSharedData?.path ),Path.GetFileName(file))  );
                    }
                catch ( Exception ) { }
                }
            }


        private void ExportConfig_Clicked(object sender, RoutedEventArgs e) {
            if (!m_connected) {
                MessageBox.Show("Inject into a game first!");
                return;
            }

            if (m_lastSharedData == null) {
                MessageBox.Show("No game connection detected.");
                return;
            }

            var dir = GetGlobalGameDir(m_lastSelectedProcessName);
            if (dir == null) {
                return;
            }

            if (!Directory.Exists(dir)) {
                MessageBox.Show("Directory does not exist.");
                return;
            }

            var exportedConfigsDir = GetGlobalDirPath() + "\\ExportedConfigs";

            if (!Directory.Exists(exportedConfigsDir)) {
                Directory.CreateDirectory(exportedConfigsDir);
            }

            GameConfig.CreateZipFromDirectory(dir, exportedConfigsDir + "\\" + m_lastSelectedProcessName + ".zip");
            NavigateToDirectory(exportedConfigsDir);
        }

        private void ImportConfig_Clicked ( object sender, RoutedEventArgs e )
            {
                ImportConfig_Impl ( GameConfig.BrowseForImport ( GetGlobalDirPath ( ) ) );    
            }

        private void ImportConfig_Impl ( string? importPath )
            { 
            if (importPath == null) {
                return;
            }

            var gameName = System.IO.Path.GetFileNameWithoutExtension(importPath);
            if (gameName == null) {
                MessageBox.Show("Invalid filename");
                return;
            }

            var globalDir = GetGlobalDirPath();
            var gameGlobalDir = globalDir + "\\" + gameName;

            try {
                if (!Directory.Exists(gameGlobalDir)) {
                    Directory.CreateDirectory(gameGlobalDir);
                }

                bool wantsExtract = true;

                if (GameConfig.ZipContainsDLL(importPath)) {
                    string message = "The selected config file includes a DLL (plugin), which may execute actions on your system.\n" +
                                     "Only import configs with DLLs from trusted sources to avoid potential risks.\n" +
                                     "Do you still want to proceed with the import?";
                    var dialog = new YesNoDialog("DLL Warning", message);
                    dialog.ShowDialog();

                    wantsExtract = dialog.DialogResultYes;
                }

                if (wantsExtract) {
                    var finalGameName = GameConfig.ExtractZipToDirectory(importPath, gameGlobalDir, gameName);

                    if (finalGameName == null) {
                        MessageBox.Show("Failed to extract the ZIP file.");
                        return;
                    }

                    var finalDirectory = System.IO.Path.Combine(globalDir, finalGameName);
                    NavigateToDirectory(finalDirectory);

                    RefreshCurrentConfig();


                    if (m_connected) {
                        SharedMemory.SendCommand(SharedMemory.Command.ReloadConfig);
                    }
                }
            } catch (Exception ex) {
                MessageBox.Show("An error occurred: " + ex.Message);
            }
        }
        private void ImportConfig_PreviewDragEnter ( object sender, DragEventArgs e )
            {
            e.Handled = true;
            }
        private void ImportConfig_PreviewDragOver ( object sender, DragEventArgs e )
            {
            e.Handled = true;
            }

        private void ImportConfig_Drop ( object sender, DragEventArgs e )
            {
            if ( e.Data.GetDataPresent ( DataFormats.FileDrop ))
                {
                string [ ] files = ( string [ ] ) e.Data.GetData ( DataFormats.FileDrop );
                var file = files [ 0 ];
                try
                    {
                    var ext = Path.GetExtension ( file );
                    if (ext.Equals(".zip" ))
                        ImportConfig_Impl ( file );
                    //experimental switch to allow copying any file into the general config dir with handling to put lua scripts and plugins in their respective dirs
                    else if (m_currentConfigPath is not null &&
                    m_ExperimentalSettingsCheckbox.IsChecked == true )
                        {
                        var confDir = Path.GetDirectoryName ( m_currentConfigPath );
                        var newPath = ext.Equals ( ".dll" ) ? 
                            Path.Combine ( confDir, "plugins", Path.GetFileName ( file ) ) : ext.Equals ( ".lua" ) ? 
                            Path.Combine ( confDir, "scripts", Path.GetFileName ( file ) ) : Path.Combine ( confDir, Path.GetFileName ( file ) );
                        if ( !Directory.Exists ( Path.GetDirectoryName ( newPath ) ) ) Directory.CreateDirectory ( Path.GetDirectoryName ( newPath ) );
                            File.Copy ( file,  newPath);
                        MessageBox .Show( $"Copied {Path.GetFileName ( file )} to config directory" );
                        }
                    }
                catch ( Exception ) { }
                }
            }

        private bool m_virtualDesktopWarned = true;
        private bool m_virtualDesktopChecked = true;
        private void Check_VirtualDesktop() {
            if (m_virtualDesktopWarned || m_ignoreFutureVDWarnings) {
                return;
            }

            if (IsExecutableRunning("VirtualDesktop.Streamer")) {
                m_virtualDesktopWarned = true;
                var dialog = new VDWarnDialog();
                dialog.ShowDialog();

                if (dialog.DialogResultOK) {
                    if (dialog.HideFutureWarnings) {
                        m_ignoreFutureVDWarnings = true;
                    }
                }
            }
        }

        private void Update_LaunchButton ( ) {
            if ( m_connected || m_pid != 0 && !Process.GetProcessById(m_pid).HasExited ) {
                m_LaunchButton.Content = "Terminate Connected Process";
                return;
            }
            else {
                m_LaunchButton.Content = "Launch and Inject";

            }
        }


        private void MainWindow_Update() {
            Update_InjectorConnectionStatus();
            Update_InjectStatus();
            if ( m_launchMode )
                {
                
                TimeSpan sixtySeconds = TimeSpan.FromSeconds ( 60);
                Update_LaunchButton ( );
                if ( m_pid != 0 )
                    {
                    if ( Process.GetProcessById ( m_pid ).HasExited )
                        {
                        m_pid = 0;
                        m_connected = false;
                        }
                    else if (ClosedOrUnresponsive(Process.GetProcessById(m_pid)) && m_ExperimentalSettingsCheckbox.IsChecked == true )
                    //else if ( !Process.GetProcessById ( m_pid ).Responding )
                        {
                        DateTime now = DateTime.Now;
                        if (now - m_lastAutoInjectTime >sixtySeconds )
                            {
                            SharedMemory.SendCommand ( SharedMemory.Command.Quit );
                            }
                        }
                    }
                }
            if (m_virtualDesktopChecked == false) {
                m_virtualDesktopChecked = true;
                Check_VirtualDesktop();
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            m_mainWindowSettings.OpenXRRadio = m_openxrRadio.IsChecked == true;
            m_mainWindowSettings.OpenVRRadio = m_openvrRadio.IsChecked == true;
            m_mainWindowSettings.NullifyVRPluginsCheckbox = m_nullifyVRPluginsCheckbox.IsChecked == true;
            m_mainWindowSettings.IgnoreFutureVDWarnings = m_ignoreFutureVDWarnings;
            m_mainWindowSettings.FocusGameOnInjection = m_focusGameOnInjectionCheckbox.IsChecked == true;
            m_mainWindowSettings.ExperimentalSettingsCheckbox = m_ExperimentalSettingsCheckbox.IsChecked == true;
            m_mainWindowSettings.Save();
        }

        private string m_lastDisplayedWarningProcess = "";
        private string[] m_discouragedPlugins = {
            "OpenVR",
            "OpenXR",
            "Oculus"
        };

        private string? AreVRPluginsPresent_InEngineDir(string enginePath) {
            string pluginsPath = enginePath + "\\Binaries\\ThirdParty";

            if (!Directory.Exists(pluginsPath)) {
                return null;
            }

            foreach (string discouragedPlugin in m_discouragedPlugins) {
                string pluginPath = pluginsPath + "\\" + discouragedPlugin;

                if (Directory.Exists(pluginPath)) {
                    return pluginsPath;
                }
            }
            return null;
        }

        private string? AreVRPluginsPresent(string gameDirectory) {
            try {
                var parentPath = gameDirectory;

                for (int i = 0; i < 10; ++i) {
                    parentPath = System.IO.Path.GetDirectoryName(parentPath);

                    if (parentPath == null) {
                        return null;
                    }

                    if (Directory.Exists(parentPath + "\\Engine")) {
                        return AreVRPluginsPresent_InEngineDir(parentPath + "\\Engine");
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"Exception caught: {ex}");
            }

            return null;
        }

        private bool IsUnrealEngineGame(string gameDirectory, string targetName) {
            try {
                if (targetName.ToLower().EndsWith("-win64-shipping")) {
                    return true;
                }

                if (targetName.ToLower().EndsWith("-wingdk-shipping")) {
                    return true;
                }
                //kind of redundant now that I added the cleanup class
                var confDir = Path.Combine ( GetGlobalDir ( ), targetName ) ;
                if ( Directory.Exists ( confDir) )
                    {
                    if ( File.Exists ( Path.Combine ( confDir, "sdkdump", "FName.cpp" ) ) ) return true;
                    var uobj = Path.Combine ( confDir, "uobjecthook" );
                    if ( Directory.Exists ( uobj ) )
                        {
                        foreach(var file in Directory.GetFiles ( uobj ) )
                            {
                            if ( file.EndsWith ( "_props.json" ) ) return true;
                            }
                        }
                    }
                // Check if going up the parent directories reveals the directory "\Engine\Binaries\ThirdParty".
                var parentPath = gameDirectory;
                for (int i = 0; i < 10; ++i) {  // Limit the number of directories to move up to prevent endless loops.
                    if (parentPath == null) {
                        return false;
                    }

                    if (Directory.Exists(parentPath + "\\Engine\\Binaries\\ThirdParty")) {
                        return true;
                    }

                    if (Directory.Exists(parentPath + "\\Engine\\Binaries\\Win64")) {
                        return true;
                    }

                    parentPath = System.IO.Path.GetDirectoryName(parentPath);
                }
            } catch (Exception ex) {
                Console.WriteLine($"Exception caught: {ex}");
            }

            return false;
        }

        private string IniToString(IConfiguration config) {
            string result = "";

            foreach (var kv in config.AsEnumerable()) {
                result += kv.Key + "=" + kv.Value + "\n";
            }

            return result;
        }

        private void SaveCurrentConfig() {
            try {
                if (m_currentConfig == null || m_currentConfigPath == null) {
                    return;
                }
                var iniStr = IniToString ( m_currentConfig );
                if (m_launchMode && !string.IsNullOrEmpty ( m_LaunchModeCmdLine.Text ) )
                    {
                    m_LaunchModeArgs = m_LaunchModeCmdLine.Text;
                    if (!iniStr.Contains( "Frontend_LauncherCommandLine=" ) )
                        iniStr += "\nFrontend_LauncherCommandLine=" + m_LaunchModeArgs;
                    }
       
                Debug.Print(iniStr);

                File.WriteAllText(m_currentConfigPath, iniStr);

                if (m_connected) {
                    SharedMemory.SendCommand(SharedMemory.Command.ReloadConfig);
                }
            } catch(Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        private void TextChanged_Value(object sender, RoutedEventArgs e) {
            try {
                if (m_currentConfig == null || m_currentConfigPath == null) {
                    return;
                }

                var textBox = (TextBox)sender;
                var keyValuePair = (GameSettingEntry)textBox.DataContext;

                // For some reason the TextBox.text is updated but the keyValuePair.Value isn't at this point.
                bool changed = m_currentConfig[keyValuePair.Key] != textBox.Text || keyValuePair.Value != textBox.Text;
                var newValue = textBox.Text;

                if (changed) {
                    RefreshCurrentConfig();
                }

                m_currentConfig[keyValuePair.Key] = newValue;
                RefreshConfigUI();

                if (changed) {
                    SaveCurrentConfig();
                }
            } catch(Exception ex) { 
                Console.WriteLine(ex.ToString()); 
            }
        }

        private void ComboChanged_Value(object sender, RoutedEventArgs e) {
            try {
                if (m_currentConfig == null || m_currentConfigPath == null) {
                    return;
                }

                var comboBox = (ComboBox)sender;
                var keyValuePair = (GameSettingEntry)comboBox.DataContext;

                bool changed = m_currentConfig[keyValuePair.Key] != keyValuePair.Value;
                var newValue = keyValuePair.Value;

                if (changed) {
                    RefreshCurrentConfig();
                }

                m_currentConfig[keyValuePair.Key] = newValue;
                RefreshConfigUI();

                if (changed) {
                    SaveCurrentConfig();
                }
            } catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }
        }

        private void CheckChanged_Value(object sender, RoutedEventArgs e) {
            try {
                if (m_currentConfig == null || m_currentConfigPath == null) {
                    return;
                }

                var checkbox = (CheckBox)sender;
                var keyValuePair = (GameSettingEntry)checkbox.DataContext;

                bool changed = m_currentConfig[keyValuePair.Key] != keyValuePair.Value;
                string newValue = keyValuePair.Value;

                if (changed) {
                    RefreshCurrentConfig();
                }

                m_currentConfig[keyValuePair.Key] = newValue;
                RefreshConfigUI();

                if (changed) {
                    SaveCurrentConfig();
                }
            } catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }
        }

        private void RefreshCurrentConfig() {
            if (m_currentConfig == null || m_currentConfigPath == null) {
                return;
            }

            InitializeConfig_FromPath(m_currentConfigPath);
        }

        private void RefreshConfigUI() {
            if (m_currentConfig == null) {
                return;
            }

            var vanillaList = m_currentConfig.AsEnumerable().ToList();
            vanillaList.Sort((a, b) => a.Key.CompareTo(b.Key));

            List<GameSettingEntry> newList = new List<GameSettingEntry>();

            foreach (var kv in vanillaList) {
                if (!string.IsNullOrEmpty(kv.Key) && !(kv.Key == "Frontend_LauncherCommandLine" ) && !string.IsNullOrEmpty(kv.Value)) {
                    Dictionary<string, string> comboValues = new Dictionary<string, string>();
                    string tooltip = "";

                    if (ComboMapping.KeyEnums.ContainsKey(kv.Key)) {
                        var valueList = ComboMapping.KeyEnums[kv.Key];

                        if (valueList != null && valueList.ContainsKey(kv.Value)) {
                            comboValues = valueList;
                        }
                    }

                    if (GameSettingTooltips.Entries.ContainsKey(kv.Key)) {
                        tooltip = GameSettingTooltips.Entries[kv.Key];
                    }

                    newList.Add(new GameSettingEntry { Key = kv.Key, Value = kv.Value, ComboValues = comboValues, Tooltip = tooltip });
                }
            }

            if (m_iniListView.ItemsSource == null) {
                m_iniListView.ItemsSource = newList;
            } else {
                foreach (var kv in newList) {
                    var source = (List<GameSettingEntry>)m_iniListView.ItemsSource;

                    var elements = source.FindAll(el => el.Key == kv.Key);

                    if (elements.Count() == 0) {
                        // Just set the entire list, we don't care.
                        m_iniListView.ItemsSource = newList;
                        break;
                    } else {
                        elements[0].Value = kv.Value;
                        elements[0].ComboValues = kv.ComboValues;
                        elements[0].Tooltip = kv.Tooltip;
                    }
                }
            }

            m_iniListView.Visibility = Visibility.Visible;
        }

        private void InitializeConfig_FromPath(string configPath) {
            var builder = new ConfigurationBuilder().AddIniFile(configPath, optional: true, reloadOnChange: false);

            m_currentConfig = builder.Build();
            m_currentConfigPath = configPath;

            foreach (var entry in MandatoryConfig.Entries) {
                if (m_currentConfig.AsEnumerable().ToList().FindAll(v => v.Key == entry.Key).Count() == 0) {
                    if ( entry.Key == "Frontend_LauncherCommandLine" ) m_LaunchModeArgs += entry.Value;
                    else m_currentConfig[entry.Key] = entry.Value;
                    }
                }

            RefreshConfigUI ();
        }

        private void InitializeConfig(string gameName) {
            var configDir = GetGlobalGameDir(gameName);
            var configPath = configDir + "\\config.txt";

            InitializeConfig_FromPath(configPath);
        }

        private bool m_isFirstProcessFill = true;

        private void ComboBox_SelectionChanged ( object sender, SelectionChangedEventArgs e )
            {
            //ComboBoxItem comboBoxItem = ((sender as ComboBox).SelectedItem as ComboBoxItem);

            try
                {
                var box = ( sender as ComboBox );
                if ( box == null || box.SelectedIndex < 0 || box.SelectedIndex > m_processList.Count )
                    {
                    return;
                    }

                var p = m_processList [ box.SelectedIndex ];
                if ( p == null || p.HasExited )
                    {
                    return;
                    }

                m_lastSelectedProcessName = p.ProcessName;
                m_lastSelectedProcessId = p.Id;

                // Search for the VR plugins inside the game directory
                // and warn the user if they exist.
                if ( m_lastDisplayedWarningProcess != m_lastSelectedProcessName && p.MainModule != null )
                    {
                    m_lastDisplayedWarningProcess = m_lastSelectedProcessName;

                    var gamePath = p.MainModule.FileName;

                    if ( gamePath != null )
                        {
                        var gameDirectory = System.IO.Path.GetDirectoryName ( gamePath );

                        if ( gameDirectory != null )
                            {
   
                            var pluginsDir = AreVRPluginsPresent ( gameDirectory );

                            if ( pluginsDir != null )
                                {
                                MessageBox.Show ( "VR plugins have been detected in the game install directory.\n" +
                                                "You may want to delete or rename these as they will cause issues with the mod.\n" +
                                                "You may also want to pass -nohmd as a command-line option to the game. This can sometimes work without deleting anything." );
                                var result = MessageBox.Show ( "Do you want to open the plugins directory now?", "Confirmation", MessageBoxButton.YesNo );

                                switch ( result )
                                    {
                                    case MessageBoxResult.Yes:
                                        NavigateToDirectory ( pluginsDir );
                                        break;
                                    case MessageBoxResult.No:
                                        break;
                                    };
                                } 
                         Check_VirtualDesktop ( );

                            m_iniListView.ItemsSource = null; // Because we are switching processes.
                            InitializeConfig ( p.ProcessName );

                            if ( !IsUnrealEngineGame ( gameDirectory, m_lastSelectedProcessName ) && !m_isFirstProcessFill )
                                {
                                MessageBox.Show ( "Warning: " + m_lastSelectedProcessName + " does not appear to be an Unreal Engine title" );
                                var result = MessageBox.Show ( "Do you want to exclude this process in the future?", "Confirmation", MessageBoxButton.YesNo );

                                switch ( result )
                                    {
                                    case MessageBoxResult.Yes:
                                        ExcludeProcess ( m_lastSelectedProcessName);
                                        break;
                                    case MessageBoxResult.No:
                                        break;
                                    };
                                
                                }
                            }

                        m_lastDefaultProcessListName = GenerateProcessName ( p );
                        }
                    }
                }
            catch ( Exception ex )
                {
                Console.WriteLine ( $"Exception caught: {ex}" );
                }
            }

        //Split implementation for launch mode 
        private void ProcessSelected ( string gamePath )
            {
            // Search for the VR plugins inside the game directory
            // and warn the user if they exist.
            if ( gamePath != null )
                {
                var gameDirectory = System.IO.Path.GetDirectoryName ( gamePath );
                CleanLocalRuntime ( );
                if ( gameDirectory != null )
                    {
                    var pluginsDir = AreVRPluginsPresent ( gameDirectory );

                    if ( pluginsDir != null )
                        {
                        MessageBox.Show ( "VR plugins have been detected in the game install directory.\n" +
                                        "You may want to delete or rename these as they will cause issues with the mod.\n" +
                                        "You may also want to pass -nohmd as a command-line option to the game. This can sometimes work without deleting anything." );
                        var result = MessageBox.Show ( "Do you want to open the plugins directory now?", "Confirmation", MessageBoxButton.YesNo );

                        switch ( result )
                            {
                            case MessageBoxResult.Yes:
                                NavigateToDirectory ( pluginsDir );
                                break;
                            case MessageBoxResult.No:
                                break;
                            };
                        }

                    Check_VirtualDesktop ( );
                    var processName = System.IO.Path.GetFileNameWithoutExtension ( gamePath );
                    m_iniListView.ItemsSource = null; // Because we are switching processes.
                    InitializeConfig ( processName );

                    if ( !IsUnrealEngineGame ( gameDirectory, processName ) && !m_isFirstProcessFill )
                        {
                        MessageBox.Show ( "Warning: " + processName + " does not appear to be an Unreal Engine title" );
                        ExcludeProcess ( processName );
                        }
                    }

                }
            }



        private void ComboBox_DropDownOpened ( object sender, System.EventArgs e ) {

                m_lastSelectedProcessName = "";
                m_lastSelectedProcessId = 0;

                FillProcessList ( );
                Update_InjectStatus ( );

                m_isFirstProcessFill = false;
            
        }

        private void Donate_Clicked(object sender, RoutedEventArgs e) {
            Process.Start(new ProcessStartInfo("https://patreon.com/praydog") { UseShellExecute = true });
        }

        private void Documentation_Clicked(object sender, RoutedEventArgs e) {
            Process.Start(new ProcessStartInfo("https://praydog.github.io/uevr-docs/") { UseShellExecute = true });
        }
        private void Discord_Clicked(object sender, RoutedEventArgs e) {
            Process.Start(new ProcessStartInfo("http://flat2vr.com") { UseShellExecute = true });
        }
        private void GitHub_Clicked(object sender, RoutedEventArgs e) {
            Process.Start(new ProcessStartInfo("https://github.com/praydog/UEVR") { UseShellExecute = true });
        }

        private void LaunchModeEnabledImpl ( )
            {
            m_CmdLineGroup.Visibility = Visibility.Visible;
            m_LaunchModeAppPicker.Visibility = Visibility.Visible;
            m_processListBox.Visibility = Visibility.Collapsed;
            m_LaunchModeText.Text = "To Inject Mode";
            m_LaunchTargetView.Visibility = string.IsNullOrEmpty ( m_launchTarget ) ? Visibility.Visible : Visibility.Collapsed;
            m_injectButton.Visibility = Visibility.Collapsed;
            m_LaunchModeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.DebugStepInto;
            if ( !IsAdministrator ( ) )
                {
                MessageBox.Show ( "Launch mode is unlikely to work without running as administrator" );
                var result = MessageBox.Show ( "Do you want to restart as admin now?", "Confirmation", MessageBoxButton.YesNo );

                switch ( result )
                    {
                    case MessageBoxResult.Yes:
                        RestartAsAdminButton_Impl (true );
                        break;
                    case MessageBoxResult.No:
                        break;
                    };
                }
            }
        private void LaunchModeDisabledImpl ( )
            {
            if (!string.IsNullOrEmpty(m_launchTarget) && !m_connected )
                {
                //allows users to select an exe in launch mode and get the same behavior as launching with an attached exe,
                //meaning we can skip process list fill
                m_commandLineAttachExe = Path.GetFileNameWithoutExtension(m_launchTarget);
                m_launchTarget = "";
                }
            m_CmdLineGroup.Visibility = Visibility.Collapsed;
            m_LaunchModeAppPicker.Visibility = Visibility.Collapsed;
            m_LaunchModeText.Text = "To Launch Mode";
            m_LaunchButton.Visibility = Visibility.Collapsed;
            m_injectButton.Visibility = Visibility.Visible;
            m_processListBox.Visibility = Visibility.Visible;
            m_LaunchTargetView.Visibility = Visibility.Collapsed;
            m_LaunchModeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Launch;
            }

        private void LaunchModeButton_Clicked ( object sender, RoutedEventArgs e){
          // m_LaunchModeButton.Content = m_launchMode ? "Switch to Inject Mode" : "Switch to Launch Mode";
            m_launchMode = !m_launchMode;
            if ( m_launchMode )
                {
                LaunchModeEnabledImpl ( );
                }
            else {
                LaunchModeDisabledImpl ( );
                }
            MainWindow_Update ( );
            }

        private void AppPicker_Clicked(object sender, RoutedEventArgs e )
            {
            //shouldnt ever happen
            if ( m_connected ) return;
            m_launchTarget = AppPicker_FileDialog ( "Select an Unreal Engine game to launch", "" );
            if (!AppPicker_VerifySelection ( ))
                {
                m_launchTarget = "";
                MessageBox.Show ( "Not a valid selection" );
                return;
                }
            ProcessSelected ( m_launchTarget );
            m_LaunchTargetView.Visibility = Visibility.Visible;
            m_LaunchTargetView.Text = m_launchTarget;
            m_openGameDirectoryBtn.Visibility = Visibility.Visible;
            m_LaunchButton.Visibility = Visibility.Visible;
            RefreshConfigUI ( );
            Update_InjectorConnectionStatus ( );
            }


        private string AppPicker_FileDialog(string message, string testPath )
            {
            var openFileDialog = new OpenFileDialog
                {
                DefaultExt = ".exe",
                Filter = "Executable Files (*.exe)|*.exe",
                };
            if ( !string.IsNullOrEmpty ( testPath ) )
                {
                openFileDialog.InitialDirectory = Path.GetDirectoryName ( testPath );
                openFileDialog.FileName = Path.GetFileName ( testPath );
                }
            openFileDialog.Title = message;
            openFileDialog.ShowDialog ( );
            return openFileDialog.FileName;
            }

        private bool AppPicker_VerifySelection ( )
            {
            if ( string.IsNullOrEmpty ( m_launchTarget ) ) return false;
            if ( m_launchTarget.ToLower ( ).EndsWith ( "win64-shipping.exe" ) ) return true;
                var targetDir = Path.GetFullPath ( Path.GetDirectoryName ( m_launchTarget ) );
                if ( Directory.Exists ( Path.Combine ( targetDir, "Engine" ) ) )
            //check if user selected the wrong 
                {
                        foreach(var dir in Directory.GetDirectories( targetDir ) )
                            {
                            if ( dir == "Engine" ) continue;
                            var maybeGameDir = Path.Combine ( dir, "Binaries", "Win64" );
                            if ( Directory.Exists ( maybeGameDir ) )
                                {
                                foreach(var file in Directory.GetFiles(maybeGameDir))
                                    {
                                    if (file.EndsWith("Win64-Shipping.exe"))
                                        {
                                        //forces user to hopefully realize their mistake and sets the filepath for next time which is the main reason I don't automate this
                                        m_launchTarget = AppPicker_FileDialog ( "Confirm the real game path", Path.GetFullPath ( file ) );
                                        return true;
                                        }
                                    }
                                }
                            }      
                }
            return false;
            }

        private void AppPicker_PreviewDragOver(object sender, DragEventArgs e )
            {
            e.Handled = true;
            }

        private void AppPicker_Drop ( object sender, DragEventArgs e )
            {
            if ( e.Data.GetDataPresent ( DataFormats.FileDrop ) && m_pid == 0 && m_launchTarget == "")
                {
                string [ ] files = ( string [ ] ) e.Data.GetData ( DataFormats.FileDrop );
                var file = files [ 0 ];
                try
                    {
                    m_launchTarget = file;
                    if (!AppPicker_VerifySelection() )
                        {
                        m_launchTarget = "";
                        return;
                        }
                     ProcessSelected ( m_launchTarget );
                    m_LaunchTargetView.Visibility = Visibility.Visible;
                    m_LaunchTargetView.Text = m_launchTarget;
                    m_LaunchButton.Visibility = Visibility.Visible;
                    m_openGameDirectoryBtn.Visibility = Visibility.Visible;
                    Update_InjectorConnectionStatus ( );
                    }
                catch ( Exception ) { }
                }
            }

        private void CommandLineText_Updated(object sender, RoutedEventArgs e)
            {

            if ( !String.IsNullOrEmpty ( m_LaunchModeCmdLine.Text ) ) m_LaunchModeArgs = m_LaunchModeCmdLine.Text;
            SaveCurrentConfig ( );
            }

        private void CleanLocalRuntime ( )
            {
            try
                {
                if ( m_connected || string.IsNullOrEmpty ( m_launchTarget ) ) return;
                //var engineDir = Path.Combine ( m_launchTarget, "..", "..", "..", "Engine" );
                var openxr = Path.Combine ( Path.GetDirectoryName ( m_launchTarget ), "openxr_loader.dll" );
                var openvr = Path.Combine ( Path.GetDirectoryName ( m_launchTarget ), "openvr_api.dll" );
                if ( File.Exists ( openxr ) ) File.Delete ( openxr );
                if ( File.Exists ( openvr ) ) File.Delete ( openvr );
                }
            catch ( Exception ) { }
            }

        private void Launch_Clicked ( object sender, RoutedEventArgs e )
            {
            if ( string.IsNullOrEmpty ( m_launchTarget ) && !m_connected ) return;
            Launch_Clicked_Impl ( );
            }

        private void Launch_Clicked_Impl (  )
            {
            TimeSpan thirtySeconds = TimeSpan.FromSeconds ( 30 );
            var now = DateTime.Now;

            if ( m_pid != 0 )
                {
                //ignore early misclicks to terminate 
                if ( DateTime.Now - m_lastAutoInjectTime > thirtySeconds )
                    {
                    Process target = Process.GetProcessById ( m_pid );
                    if ( target == null || target.HasExited )
                        {
                        return;
                        }

                    target.WaitForInputIdle ( 100 );

                    SharedMemory.SendCommand ( SharedMemory.Command.Quit );

                    if ( target.WaitForExit ( 2000 ) )
                        {
                        return;
                        }

                    target.Kill ( );

                    }
                return;
                }

            Process? process = null;
            string runtimeName;

            if ( m_openvrRadio.IsChecked == true )
                {
                runtimeName = "openvr_api.dll";
                }
            else if ( m_openxrRadio.IsChecked == true )
                {
                runtimeName = "openxr_loader.dll";
                }
            else
                {
                //steam can also use this so imo this is a better default
                runtimeName = "openxr_loader.dll";
                }
            var creationFlag = m_startSuspended ? 4u : 0u;

            var cd = Directory.GetCurrentDirectory ( );
            DirectoryInfo dInfo = new DirectoryInfo ( m_launchTarget );
            var wdir = dInfo.Parent.FullName;
            var sid = new SecurityIdentifier ( "S-1-15-2-1" );
            var access = new DirectorySecurity ( );
            access.AddAccessRule ( new FileSystemAccessRule ( identity: WindowsIdentity.GetCurrent ( ).User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow ) );
            dInfo.SetAccessControl ( access );
            Injector.PROCESS_INFORMATION pi;
            Injector.STARTUPINFO si = new Injector.STARTUPINFO ( );
           
            //having openxr_loader in the game dir can cause failure to launch sometimes so we clean up beforehand 
            var localRuntime = Path.Combine ( wdir, runtimeName );
            if ( File.Exists ( localRuntime ) ) File.Delete ( localRuntime );
            //we already specify launching the proc from game dir but this may help a bit with some cases like injecting across drives. realistically could likely be removed but needs further tests
            Directory.SetCurrentDirectory ( wdir );
            var backendSrc = m_ExperimentalSettingsCheckbox.IsChecked == true ? Path.Combine ( GetGlobalDir ( ), "uevr-nightly" ) : Path.GetDirectoryName ( Process.GetCurrentProcess ( ).MainModule.FileName );
            //TODO replace with tool snapshot method
            var dllPaths = new List<string> ( ) { Path.Combine ( backendSrc, "UEVRBackend.dll" ), Path.Combine ( backendSrc, runtimeName ) };

            foreach(var p in Process.GetProcessesByName ( Path.GetFileName ( m_launchTarget ) ) )
                {
                if ( !Injector.InjectDll ( p.Id, dllPaths [0] ) ) p.Kill ( );
                else
                    {
                    m_pid = p.Id;
                    break;
                    }
                }
            if ( m_pid != 0 ) goto SUCCESS;
            if ( !Injector.CreateProcess ( m_launchTarget, m_LaunchModeArgs, IntPtr.Zero, IntPtr.Zero, true, creationFlag, IntPtr.Zero, wdir, ref si, out pi ) )
                {
                MessageBox.Show ( "Failed to launch process" );
                return;
                }


            if ( !Injector.InjectDlls ( pi.hProcess, dllPaths))
                {
                MessageBox.Show ( "Failed to inject backend into process" );
                if ( m_startSuspended )
                    Process.GetProcessById ( pi.dwProcessId ).Kill ( );
                return;
                }
             m_pid = pi.dwProcessId;
            if ( m_startSuspended ) Injector.ResumeThread ( pi.hThread );
    SUCCESS:
            //    //generally runtime in gamedir is more effective but if we can't write to there we can try injecting from uevr folder 
            File.Copy ( Path.Combine ( AppContext.BaseDirectory, runtimeName ), localRuntime );           
            m_lastAutoInjectTime = now;
            m_processList.Add ( Process.GetProcessById ( m_pid ) );
            InitializeConfig ( Path.GetFileNameWithoutExtension ( m_launchTarget ) );
            try
                {
                if ( m_currentConfig != null )
                    {
                    if ( m_currentConfig [ "Frontend_RequestedRuntime" ] != runtimeName )
                        {
                        m_currentConfig [ "Frontend_RequestedRuntime" ] = runtimeName;
                        RefreshConfigUI ( );
                        SaveCurrentConfig ( );
                        }
                    }
                }
            catch ( Exception )
                {

                }
            Update_InjectorConnectionStatus ( );
            Directory.SetCurrentDirectory ( cd );
            if ( m_focusGameOnInjectionCheckbox.IsChecked == true )
                {
                SwitchToThisWindow ( Process.GetProcessById ( m_pid ).MainWindowHandle, true );
                }
            }


        //private async Task HijackGame ( )
        //    {
        //    while ( !IsGameRunning ( ) )
        //        {
        //        await Task.Delay ( 10 );
        //        }

        //    if ( IsGameRunning ( ) )
        //        {
        //        var delay = conf.InjectDelay;
        //        await Task.Delay ( ( int ) delay );
        //        Process.GetProcesses ( )
        //            .Where ( x => x.ProcessName == Path.GetFileNameWithoutExtension ( conf.LoadTarget ) )
        //            .ToList ( )
        //            .ForEach ( x => ProcessUtils.InjectDlls ( x.Id, conf.DllPaths ) );
        //        }
        //    }


        private async Task HandleInjection ( PROCESS_INFORMATION pi, DateTime creationTime, int delay, List<string> dllPaths )
            {
            var backendSrc = m_ExperimentalSettingsCheckbox.IsChecked == true ? Path.Combine ( GetGlobalDir ( ), "uevr-nightly" ) : Path.GetDirectoryName ( Process.GetCurrentProcess ( ).MainModule.FileName );
            await Task.Delay ( delay * 500 );
            if ( !Injector.InjectDlls ( pi.hProcess, dllPaths) )
                Console.WriteLine ( "Failed to inject" );
            int tries = 0;
            bool result = false;
            do
                {
                if ( !Injector.InjectDlls ( pi.hProcess, dllPaths                                                   ) ) 
                    {
                    tries++;
                    result = false;
                    Console.WriteLine ( "Failed to inject" );
                    }
                else result = true;
                } while ( !result && tries < 10 );
            }

        private void Inject_Clicked ( object sender, RoutedEventArgs e ) {
                // "Terminate Connected Process"
                if ( m_connected ) {
                    try {
                        var pid = m_lastSharedData?.pid;

                        if ( pid != null ) {
                            var target = Process.GetProcessById ( ( int ) pid );

                            if ( target == null || target.HasExited ) {
                                return;
                            }

                            target.WaitForInputIdle ( 100 );

                            SharedMemory.SendCommand ( SharedMemory.Command.Quit );
                            //since clicking terminate in injection mode can terminate launch mode procs and we allow free switching
                            CleanLocalRuntime ( );

                            if ( target.WaitForExit ( 2000 ) ) {
                                return;
                            }

                            target.Kill ( );
                        }
                    } catch ( Exception ) {

                    }

                    return;
                }

                var selectedProcessName = m_processListBox.SelectedItem;

                if ( selectedProcessName == null ) {
                    return;
                }

                var index = m_processListBox.SelectedIndex;
                var process = m_processList [ index ];

                if ( process == null ) {
                    return;
                }

                // Double check that the process we want to inject into exists
                // this can happen if the user presses inject again while
                // the previous combo entry is still selected but the old process
                // has died.
                try {
                    var verifyProcess = Process.GetProcessById ( m_lastSelectedProcessId );

                    if ( verifyProcess == null || verifyProcess.HasExited || verifyProcess.ProcessName != m_lastSelectedProcessName ) {
                        var processes = Process.GetProcessesByName ( m_lastSelectedProcessName );

                        if ( processes == null || processes.Length == 0 || !AnyInjectableProcesses ( processes ) ) {
                            return;
                        }

                        foreach ( var candidate in processes ) {
                            if ( IsInjectableProcess ( candidate ) == InjectableProcCode.Valid ) {
                                process = candidate;
                                break;
                            }
                        }

                        m_processList [ index ] = process;
                        m_processListBox.Items [ index ] = GenerateProcessName ( process );
                        m_processListBox.SelectedIndex = index;
                    }
                } catch ( Exception ex ) {
                    MessageBox.Show ( ex.Message );
                    return;
                }

                string runtimeName;

                if ( m_openvrRadio.IsChecked == true ) {
                    runtimeName = "openvr_api.dll";
                } else if ( m_openxrRadio.IsChecked == true ) {
                    runtimeName = "openxr_loader.dll";
                } else {
                    runtimeName = "openvr_api.dll";
                }

                if ( m_nullifyVRPluginsCheckbox.IsChecked == true ) {
                    IntPtr nullifierBase;
                    if ( Injector.InjectDll ( process.Id, "UEVRPluginNullifier.dll", out nullifierBase ) && nullifierBase.ToInt64 ( ) > 0 ) {
                        if ( !Injector.CallFunctionNoArgs ( process.Id, "UEVRPluginNullifier.dll", nullifierBase, "nullify", true ) ) {
                            MessageBox.Show ( "Failed to nullify VR plugins." );
                        }
                    } else {
                        MessageBox.Show ( "Failed to inject plugin nullifier." );
                    }
                }

                if ( Injector.InjectDll ( process.Id, runtimeName ) ) {
                    try {
                        if ( m_currentConfig != null ) {
                            if ( m_currentConfig [ "Frontend_RequestedRuntime" ] != runtimeName ) {
                                m_currentConfig [ "Frontend_RequestedRuntime" ] = runtimeName;
                                RefreshConfigUI ( );
                                SaveCurrentConfig ( );
                            }
                        }
                    } catch ( Exception ) {

                    }

                    Injector.InjectDll ( process.Id, "UEVRBackend.dll" );
                }

                if ( m_focusGameOnInjectionCheckbox.IsChecked == true )
                {
                    SwitchToThisWindow ( process.MainWindowHandle, true );
                }
            }

            private string GenerateProcessName ( Process p ) {
                return p.ProcessName + " (pid: " + p.Id + ")" + " (" + p.MainWindowTitle + ")";
            }

            [DllImport ( "kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi )]
            [return: MarshalAs ( UnmanagedType.Bool )]
            private static extern bool IsWow64Process ( [In] IntPtr hProcess, [Out] out bool wow64Process );



        private InjectableProcCode IsInjectableProcess ( Process process ) {
            var procName = Path.GetFileNameWithoutExtension ( process.ProcessName );
            try {
                    if ( Environment.Is64BitOperatingSystem ) {
                        try {
                            bool isWow64 = false;
                            if ( IsWow64Process ( process.Handle, out isWow64 ) && isWow64 ) {
                                return InjectableProcCode.Exclude;
                            }
                        } catch {
                            // If we threw an exception here, then the process probably can't be accessed anyways.
                            return InjectableProcCode.Exclude;
                        }
                    }
                    
                    if ( process.MainWindowTitle.Length == 0 ) {
                        return InjectableProcCode.Exclude;
                    }

                    if ( process.Id == Process.GetCurrentProcess ( ).Id ) {
                        return InjectableProcCode.Exclude;
                    }

                    if ( !m_executableFilter.IsValidExecutable ( process.ProcessName.ToLower ( ) ) ) {
                        return InjectableProcCode.Exclude;
                    }

                bool kernel32 = false;
                int moduleCount = 0;
                try
                    {
                    moduleCount += process.Modules.Count;
                    foreach ( ProcessModule module in process.Modules )
                        {
                        if ( module.ModuleName == null )
                            {
                            continue;
                            }

                        string moduleLow = module.ModuleName.ToLower ( );
                        if ( moduleLow == "d3d11.dll" || moduleLow == "d3d12.dll" )
                            {
                            if ( IsUnrealEngineGame ( Path.GetDirectoryName ( process.MainModule.FileName ), procName ))
                            return InjectableProcCode.Valid;
                            }
                        if ( moduleLow == "kernel32.dll" )
                            {
                            kernel32 = true;
                            }
                        }
                    }
                catch ( Exception ) { }

                //I'm actually not sure yet if it will fail this check but the hope is to provide some information when games are protected rather than current behavior which just doesnt show the games at all. Need to check that the process is a game since this method is called from the iterator to fill the list and I don't want to show multiple messages if there are other processes that don't have kernel32
                //so turns out we just can't get the modules  or the file path
                if ( !kernel32 || moduleCount == 0)
                    {
                    if( procName.EndsWith ("win64-shipping"))
                        MessageBox.Show ( "Detected a possible Unreal Engine title running but could not find kernel32.dll in the process or enumerate process modules. This means injection will fail, likely due to an anticheat or Windows security settings.\nFirst try restarting as admin and consider disabling security settings temporarily.\nYou can try using launch mode to inject before any interfering process can launch.\nThis usually works but doesn't prevent crashes from accessing guarded memory or scans for function hooks. Proceed with caution" );     
                    return InjectableProcCode.Warn;
                    }
                List<string> exclusions = SyncExclusions(new List<string> ( ));
                // Check if the process name is in the excluded list
                if ( exclusions.Contains( Path.GetFileNameWithoutExtension(process.ProcessName) ) )
                    {
                    return InjectableProcCode.Exclude;
                    }
                return InjectableProcCode.Exclude;
                } catch ( Exception ex ) {
                    Console.WriteLine ( ex.ToString ( ) );
                    return InjectableProcCode.Exclude;
                }
            }

            private bool AnyInjectableProcesses ( Process [ ] processList ) {
            foreach ( Process process in processList ) {
                var procCode = IsInjectableProcess ( process );
                if ( procCode == InjectableProcCode.Valid )
                    return true;
                else if ( procCode == InjectableProcCode.Exclude ) 
                    return false;
                else if (procCode == InjectableProcCode.Warn)
                    {
                    MessageBox.Show ( "This might be a valid game but it doesn't seem to be injectable." );
                    var result = MessageBox.Show ( $"Do you want to exclude {process.ProcessName}?", "Confirmation", MessageBoxButton.YesNo );

                    switch ( result )
                        {
                        case MessageBoxResult.Yes:
                            ExcludeProcess ( Path.GetFileNameWithoutExtension ( process.ProcessName ) );
                            return false;
                        case MessageBoxResult.No:
                            return true;
                        };
                    }
                }
                return false;
            }
        

        private SemaphoreSlim m_processSemaphore = new SemaphoreSlim(1, 1); // create a semaphore with initial count of 1 and max count of 1
        private string? m_lastDefaultProcessListName = null;
        private TimeSpan oneSecond = TimeSpan.FromSeconds ( 1 );

        private async void FillProcessList() {
            List<string> excludedProcesses = SyncExclusions (new List<string>() );
            // Allow the previous running FillProcessList task to finish first
            if (m_processSemaphore.CurrentCount == 0) {
                return;
            }
            await m_processSemaphore.WaitAsync();

            try {
                m_processList.Clear();
                m_processListBox.Items.Clear();

                await Task.Run(() => {
                    // get the list of processes
                    Process[] processList = Process.GetProcesses();
                    // loop through the list of processes
                    foreach ( Process process in processList) {
                        if (IsInjectableProcess(process ) == InjectableProcCode.Exclude) {
                            ExcludeProcess ( Path.GetFileNameWithoutExtension(process.ProcessName) );
                            continue;
                        }

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            m_processList.Add(process);
                            m_processList.Sort((a, b) => a.ProcessName.CompareTo(b.ProcessName));
                            m_processListBox.Items.Clear();

                            foreach (Process p in m_processList) {
                                string processName = GenerateProcessName(p);
                                m_processListBox.Items.Add(processName);

                                if (m_processListBox.SelectedItem == null && m_processListBox.Items.Count > 0) {
                                    if (m_lastDefaultProcessListName == null || m_lastDefaultProcessListName == processName) {
                                        m_processListBox.SelectedItem = m_processListBox.Items[m_processListBox.Items.Count - 1];
                                        m_lastDefaultProcessListName = processName;
                                    }
                                }
                            }
                        });
                    }

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        m_processListBox.Items.Clear();

                        foreach (Process process in m_processList) {
                            string processName = GenerateProcessName(process);
                            m_processListBox.Items.Add(processName);

                            if (m_processListBox.SelectedItem == null && m_processListBox.Items.Count > 0) {
                                if (m_lastDefaultProcessListName == null || m_lastDefaultProcessListName == processName) {
                                    m_processListBox.SelectedItem = m_processListBox.Items[m_processListBox.Items.Count - 1];
                                    m_lastDefaultProcessListName = processName;
                                }
                            }
                        }
                    });
                });
            } finally {
                m_processSemaphore.Release();
            }
        }       
        }
}

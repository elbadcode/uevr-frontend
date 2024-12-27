using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using System.Windows.Interop;

namespace UEVR
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string UniqueMutexName = "Global\\UEVRMutex";
        private const string PipeName = "UEVRFrontendPipe";
        private Mutex mutex;

        protected override void OnStartup ( StartupEventArgs e )
            {
            bool createdNew;
            mutex = new Mutex ( true, UniqueMutexName, out createdNew );

            if ( !createdNew )
                {
                NotifyExistingInstance ( e.Args );
                App.Current.Shutdown ( ); // Exit this instance
                return;
                }

            base.OnStartup ( e );
            //MainWindow = new MainWindow ( );
            //MainWindow.Show ( );
            }

        private void NotifyExistingInstance ( string [ ] args )
            {
            string arguments = string.Join ( " ", args );

            using ( NamedPipeClientStream pipeClient = new NamedPipeClientStream ( ".", PipeName, PipeDirection.Out ) )
                {
                try
                    {
                    pipeClient.Connect ( 1000 ); // Wait for 1 second for the connection

                    using ( StreamWriter sw = new StreamWriter ( pipeClient ) )
                        {
                        sw.AutoFlush = true;
                        sw.WriteLine ( arguments );
                        }
                    }
                catch ( TimeoutException )
                    {
                    MessageBox.Show ( "Could not find another instance of the application." );
                    }
                }
            }

        protected override void OnExit ( ExitEventArgs e )
            {
            if ( mutex != null )
                {
                mutex.ReleaseMutex ( );
                mutex.Close ( );
                }
            base.OnExit ( e );
            }
        }

    }

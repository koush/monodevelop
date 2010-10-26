using System;
using System.Collections.Generic;
using MonoDevelop.Projects;
using MonoDevelop.Core;
using MonoDevelop.Core.Assemblies;
using MonoDevelop.Core.Execution;
using System.IO;
using System.Threading;
using System.Text;
using System.Diagnostics;

namespace MonoDevelop.Android
{

	public class AndroidExecutionHandler : IExecutionHandler
	{
		#region IExecutionHandler implementation
		public bool CanExecute (ExecutionCommand command)
		{
			return command as AndroidExecutionCommand != null;
		}

		public IProcessAsyncOperation Execute (ExecutionCommand command, IConsole console)
		{
			AndroidExecutionCommand cmd = (AndroidExecutionCommand) command;
            
			if (!AndroidSDKInstalledCondition.IsInstalled)
				throw new InvalidOperationException("Android SDK is not installed! Please set your ANDROID_SDK environment variable, or symlink it at /Developer/android-sdk.");
			
			string adbPath = Path.Combine(AndroidSDKInstalledCondition.ANDROID_SDK, "tools/adb");
            
            if (cmd.IsAPK)
            {
                var cfg = Config.Load();
                var ant = cfg.Ant ?? "ant";
                var antProcInfo = new ProcessStartInfo(ant, "install");
                antProcInfo.WorkingDirectory = cmd.JavaProjectPath;
                antProcInfo.RedirectStandardOutput = true;
                antProcInfo.UseShellExecute = false;
                var antProc = Process.Start(antProcInfo);
                console.Out.Write(antProc.StandardOutput.ReadToEnd());
                
                string adbShellArgs = string.Format("shell 'am start -a android.intent.action.MAIN -n {0}/{0}.MonoActivity ; sleep -1'", cmd.DefaultNamespace);
                Console.WriteLine(adbShellArgs);

                var psi = new ProcessStartInfo (adbPath, adbShellArgs) {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    WorkingDirectory = cmd.LogDirectory,
                    UseShellExecute = false
                };
                
                var androidProcess = Runtime.ProcessService.StartProcess (psi, console.Out, console.Error, null);
    
                return new AndroidProcess (androidProcess);
            }
            else
            {
                string adbPushArgs = string.Format("push {0}/ /data/local/bin", cmd.LogDirectory);
                Process.Start(adbPath, adbPushArgs).WaitForExit();
            
                string outputFile = Path.GetFileName(cmd.OutputAssembly);
                string adbShellArgs = String.Format ("shell '/data/data/com.koushikdutta.mono/mono /data/local/bin/{0}'", outputFile);
                Console.WriteLine(adbShellArgs);
                
                var psi = new ProcessStartInfo (adbPath, adbShellArgs) {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    WorkingDirectory = cmd.LogDirectory,
                    UseShellExecute = false
                };
                
                var androidProcess = Runtime.ProcessService.StartProcess (psi, console.Out, console.Error, null);
    
                return new AndroidProcess (androidProcess);
            }
		}
		#endregion
	}



	class AndroidProcess : IProcessAsyncOperation, IDisposable
	{
		ProcessWrapper androidProcess;
		
		public AndroidProcess (ProcessWrapper androidProcess)
		{
			this.androidProcess = androidProcess;
		}
		
		public int ExitCode {
			get {
				//FIXME: implement
				return 0;
			}
		}
		
		public int ProcessId {
			get {
				//FIXME: implement
				return androidProcess.Id;
			}
		}
		
		void CompletionWrapper (IAsyncOperation op)
		{
			completed (op);
		}
		
		OperationHandler completed;
		
		public event OperationHandler Completed {
			add {
				lock (completed) {
					if (completed == null)
						((IProcessAsyncOperation)androidProcess).Completed += CompletionWrapper;
					completed += value;
				}
			}
			remove {
				lock (completed) {
					completed -= value;
					if (completed == null)
						((IProcessAsyncOperation)androidProcess).Completed -= CompletionWrapper;
				}
			}
		}
		
		public void Cancel ()
		{
			androidProcess.StandardInput.Write ('\n');
			androidProcess.WaitForExit (1000);
			if (!((IProcessAsyncOperation)androidProcess).IsCompleted)
				((IProcessAsyncOperation)androidProcess).Cancel ();
		}
		
		public void WaitForOutput ()
		{
			((IProcessAsyncOperation)androidProcess).WaitForCompleted ();
		}
		
		void IAsyncOperation.WaitForCompleted ()
		{
			WaitForOutput ();
		}
		
		public bool IsCompleted {
			get { return ((IProcessAsyncOperation)androidProcess).IsCompleted; }
		}
		
		public bool Success {
			get { return ((IProcessAsyncOperation)androidProcess).Success; }
		}
		
		public bool SuccessWithWarnings {
			get { return ((IProcessAsyncOperation)androidProcess).SuccessWithWarnings; }
		}
		
		public void Dispose ()
		{
			Dispose (true);
			GC.SuppressFinalize (this);
		}
		
		void Dispose (bool disposing)
		{
			if (androidProcess == null)
				return;
			androidProcess.Dispose ();
			androidProcess = null;
		}
		
		~AndroidProcess ()
		{
			Dispose (false);
		}
	}
	
}

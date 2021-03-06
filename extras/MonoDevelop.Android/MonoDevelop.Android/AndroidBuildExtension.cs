using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MonoDevelop.Core;
using MonoDevelop.Projects;
using MonoDevelop.Core.ProgressMonitoring;
using System.Xml;
using System.Text;
using System.Diagnostics;
using System.CodeDom.Compiler;
using System.Xml.Serialization;

namespace MonoDevelop.Android
{

	public class AndroidBuildExtension : ProjectServiceExtension
	{

		public AndroidBuildExtension ()
		{
		}
		
        protected override BuildResult Build (IProgressMonitor monitor, SolutionEntityItem item, ConfigurationSelector configuration)
        {
            var proj = item as AndroidProject;
            if (proj == null)
                return base.Build (monitor, item, configuration);
            
            var manifest = Path.Combine(Path.Combine(proj.BaseDirectory, "Android"), "AndroidManifest.xml");
            if (null == proj.GetProjectFile(manifest))
            {
                Console.WriteLine("AndroidManifest.xml not found. Skipping APK build.");
                return base.Build (monitor, item, configuration);
            }
            
            Console.WriteLine("Building APK.");
            var conf = proj.GetConfiguration(configuration) as AndroidProjectConfiguration;
            
            var buildResult = base.Build (monitor, item, configuration);
            if (buildResult.ErrorCount > 0)
            {
                foreach (var err in buildResult.Errors)
                {
                    if (!err.IsWarning)
                        return buildResult;
                }
            }
			
            var cfg = Config.Load();
            
            var androidPath = Path.Combine(cfg.AndroidSDK, "tools/android");
            var javaProjDir = Path.Combine(proj.ItemDirectory.ToString(), "Android");
            var javaSrcDir = Path.Combine(javaProjDir, "src");
            var args = string.Format("update project -p {0}", javaProjDir);
            Process.Start(androidPath, args).WaitForExit();
            
            var assetsDir = Path.Combine(javaProjDir, "assets");
			var dlls = Directory.GetFiles(conf.OutputDirectory, "*.dll");
			var exes = Directory.GetFiles(conf.OutputDirectory, "*.exe");
			var mdbs = Directory.GetFiles(conf.OutputDirectory, "*.mdb");
			var packagedFiles = dlls.Union(mdbs).Union(exes);
			foreach (var packageFile in packagedFiles)
			{
				Console.WriteLine(packageFile);
				File.Copy(packageFile, Path.Combine(assetsDir, Path.GetFileName(packageFile)), true);
			}
			
            monitor.BeginTask("Generating Java files.", 0);
			var thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
			foreach (var managedCode in dlls.Union(exes))
			{
				var monoReflectorArgs = thisAssembly + " " + managedCode + " " + javaSrcDir;
				Console.WriteLine(monoReflectorArgs);
	            var monoReflectorProcInfo = new ProcessStartInfo("mono", monoReflectorArgs);
	            monoReflectorProcInfo.WorkingDirectory = javaProjDir;
	            monoReflectorProcInfo.RedirectStandardOutput = true;
	            monoReflectorProcInfo.UseShellExecute = false;
	            var monoReflectorProc = Process.Start(monoReflectorProcInfo);
				string javaOutputFile;
				while (null != (javaOutputFile = monoReflectorProc.StandardOutput.ReadLine()))
				{
					Console.WriteLine(javaOutputFile);
		            monitor.Log.WriteLine(javaOutputFile);
	                if (null == proj.GetProjectFile(javaOutputFile))
	                    proj.AddFile(javaOutputFile);
				}
			}
            monitor.EndTask();

            var androidmonoDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".androidmono");
            var monojavabridgejar = Path.Combine(androidmonoDir, "com.koushikdutta.monojavabridge.jar");
            var libsDir = Path.Combine(javaProjDir, "libs");
            Directory.CreateDirectory(libsDir);
            var localcopyjar = Path.Combine(libsDir, "com.koushikdutta.monojavabridge.jar");
            File.Copy(monojavabridgejar, localcopyjar, true);
            
            monitor.BeginTask("Building APK", 0);
            var ant = cfg.Ant ?? "ant";
            var antProcInfo = new ProcessStartInfo(ant, "debug");
            antProcInfo.WorkingDirectory = javaProjDir;
            antProcInfo.RedirectStandardOutput = true;
            antProcInfo.UseShellExecute = false;
            var antProc = Process.Start(antProcInfo);
            monitor.Log.Write(antProc.StandardOutput.ReadToEnd());
            monitor.EndTask();
            
            var genPath = Path.Combine(javaProjDir, "gen");
            var rPath = Path.Combine(genPath, proj.DefaultNamespace.Replace('.', Path.DirectorySeparatorChar));
            rPath = Path.Combine(rPath, "R.java");
            if (File.Exists(rPath))
            {
                var rText = File.ReadAllText(rPath);
                rText = rText.Replace("final class", "class").Replace("static final", "static readonly").Replace("class string", "class string_").Replace("class R ", "static class R ");
                var regex = new System.Text.RegularExpressions.Regex("package (.*?);");
                rText = regex.Replace(rText, "namespace $1\n{");
                rText += "\n}";
                
                rPath = Path.Combine(proj.BaseDirectory, "R.cs");
                File.WriteAllText(rPath, rText);
                if (proj.GetProjectFile(rPath) == null)
                    proj.AddFile(rPath);
            }
            
            return buildResult;
        }
        
        [Serializable]
        public class MonoReflectorContext : MarshalByRefObject
        {
            public string OutputDirectory;
            public string AssemblyFile;
            public List<string> OutputFiles = new List<string>();
            
            public void Callback()
            {
                var mr = new MonoReflector(OutputDirectory);
                OutputFiles.AddRange(mr.Generate(GenerationFlags.None, AssemblyFile));
            }
        }
        
        protected override BuildResult Compile (IProgressMonitor monitor, SolutionEntityItem item, BuildData buildData)
        {
            return base.Compile (monitor, item, buildData);
        }
        
        protected override void Clean (IProgressMonitor monitor, SolutionEntityItem item, ConfigurationSelector configuration)
        {
            base.Clean (monitor, item, configuration);
        }

		public static System.Net.IPAddress DebuggerIP
		{
			get
			{
				return System.Net.IPAddress.Loopback;
				var ipStr = MonoDevelop.Core.PropertyService.Get<string> ("Android.Debugger.HostIP", "");
                Console.WriteLine("Android.Debugger.HostIP: {0}", ipStr);
				try {
					if (!string.IsNullOrEmpty (ipStr))
						return System.Net.IPAddress.Parse (ipStr);
				} catch (Exception e) {
					LoggingService.LogInfo ("Error parsing Debugger HostIP: {0}: {1}", ipStr, e);
				}
                
                var hostName = System.Net.Dns.GetHostName();
                Console.WriteLine("Host Name: {0}", hostName);
                var addresses = System.Net.Dns.GetHostAddresses(hostName);
                foreach (var addr in addresses)
                {
                    Console.WriteLine(addr);
                    if (addr.ToString().StartsWith("192.168.1"))
                        return addr;
                }
				
                return addresses[0];
			}
		}
		
		static Random random = new Random();
		public static int DebuggerPort {
			get {
			    //return 10000;
			    return random.Next(10000, 20000);
				//return MonoDevelop.Core.PropertyService.Get<int> ("Android.Debugger.Port", 10000);
			}
		}
		
		public static int DebuggerOutputPort {
			get {
		        //return 10001;
		        return random.Next(10000, 20000);
				//return MonoDevelop.Core.PropertyService.Get<int> ("Android.Debugger.OutputPort", 10001);
			}
		}
	}
}

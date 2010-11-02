using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MonoDevelop.Android
{
	public static class Program
	{
		public static void Main(string[] args)
		{
			if (args.Length != 2)
			{
				Console.Error.WriteLine("mono MonoDevelop.Android.dll <InputAssembly> <OutputDirectory>");
				return;
			}
			
			var mr = new MonoReflector(args[1]);
			foreach (var file in mr.Generate(GenerationFlags.None, args[0]))
			{
				Console.WriteLine(file);
			}
		}
	}
	
    [Flags]
    public enum GenerationFlags : int
    {
        None = 0,
        KeepIntermediateFiles = 1
    }

    public class MonoReflector
    {
        private string mOutputPath;
        private static string mTemplate = new StreamReader(typeof(MonoReflector).Assembly.GetManifestResourceStream("JavaClassTemplate.java")).ReadToEnd();

        /*
        public MonoReflector(params string[] files)
        {
            foreach (string assem in files)
                assems.Add(Assembly.LoadFile(assem));
        }

        public MonoReflector(params Assembly[] files)
        {
            assems.AddRange(assems);
        }
        */
        
        public MonoReflector()
        {
        }
        
        public MonoReflector(string outputPath)
        {
            mOutputPath = outputPath;
        }
        
        public string[] Generate(GenerationFlags flags, string assemblyFile)
        {
            return Generate(flags, Assembly.LoadFile(assemblyFile));
        }

        private string[] Generate(GenerationFlags flags, params Assembly[] assemblies)
        {
            List<string> ret = new List<string>();
            foreach (Assembly a in assemblies)
            {
                ret.AddRange(Generate(flags, a));
            }
            return ret.ToArray();
        }

        public string[] Generate(GenerationFlags flags, Assembly assembly)
        {
            List<string> ret = new List<string>();
            foreach (Type t in assembly.GetTypes())
            {
                if (t.IsInterface)
                    continue;
                if ((from attr in t.GetCustomAttributes(false) where attr.GetType().FullName == "MonoJavaBridge.JavaClassAttribute" || attr.GetType().FullName == "MonoJavaBridge.JavaProxyAttribute" select attr).Count() != 0)
                    continue;
                Dictionary<MethodInfo, MethodInfo> methods = new Dictionary<MethodInfo, MethodInfo>();
                HashSet<Type> interfaces = new HashSet<Type>();
                foreach (MethodInfo subm in t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(x => x.IsVirtual))
                {
                    if ((subm.MemberType & MemberTypes.Property) == MemberTypes.Property || (subm.MemberType & MemberTypes.Method) == MemberTypes.Method)
                    {
                        MethodInfo androidMethod = FindBaseForMethod(subm, null);
                        if (androidMethod != null)
                        {
                            methods.Add(subm, androidMethod);
                        }
                        else//must be implementing an interface
                        {
                            androidMethod = FindInterfaceForMethod(subm);
                            if (androidMethod != null)
                            {
                                if (!interfaces.Contains(androidMethod.DeclaringType))
                                    interfaces.Add(androidMethod.DeclaringType);
                                methods.Add(subm, androidMethod);
                            }
                        }
                    }
                }

                if (methods.Count == 0)
                    continue;

                StringBuilder linkMethods = new StringBuilder(), natives = new StringBuilder();
                //get the link methods

                var proxyName = GetClassProxyName(t);
                KeyValuePair<MethodInfo, MethodInfo>? first = null;
                foreach (KeyValuePair<MethodInfo, MethodInfo> pair in methods)
                {
                    if (first == null)
                        first = pair;
                    StringBuilder args = new StringBuilder(), jniArgs = new StringBuilder();
                    ParameterInfo[] paramInfo = null;
                    foreach (ParameterInfo p in paramInfo = pair.Key.GetParameters())
                        jniArgs.Append(GetJType(p.ParameterType));
                    for (int i = 0; i < paramInfo.Length; i++)
                    {
                        args.Append(paramInfo[i].ParameterType.FullName);
                        if (i < paramInfo.Length - 1)
                            args.Append(",");
                    }
                    linkMethods.AppendLine(string.Format("\t\tMonoBridge.link({0}.class, \"{1}\", \"({2}){3}\", \"{4}\");",
                        proxyName, pair.Key.Name, jniArgs, GetJType(pair.Key.ReturnType), args));
                    args = new StringBuilder();//reuse var

                    for (int i = 0; i < paramInfo.Length; i++)
                    {
                        args.AppendFormat("{0} {1}", GetJLangType(paramInfo[i].ParameterType), paramInfo[i].Name);
                        if (i < paramInfo.Length - 1)
                            args.Append(",");
                    }
                    //natives.AppendLine("\t@Override");
                    natives.AppendLine(string.Format("\t{0} native {1} {2}({3});",
                        pair.Key.IsPublic ? "public" : "protected", GetJLangType(pair.Value.ReturnType), pair.Key.Name, args));
                }
                string basePath = mOutputPath ?? Path.GetDirectoryName(t.Assembly.Location);
                string proxyNamespace = t.Namespace;
                if (proxyNamespace.StartsWith("java."))
                    proxyNamespace = proxyNamespace.Replace("java.", "internal.java.");
                basePath = Path.Combine(basePath, proxyNamespace.Replace('.', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(basePath);
                string outputFile = Path.Combine(basePath, proxyName + ".java");
                ret.Add(outputFile);
                StringBuilder interfacesText = new StringBuilder();
                if (interfaces.Count > 0)
                {
                    foreach (var iface in interfaces)
                    {
                        interfacesText.AppendFormat(", {0}", iface.FullName.Replace('+', '.').Replace('_', '.'));
                    }
                }
                File.WriteAllText(outputFile,
                    string.Format(mTemplate, proxyNamespace, proxyName, t.BaseType.FullName == "java.lang.Object" ? "com.koushikdutta.monojavabridge.MonoProxyBase" : t.BaseType.FullName, linkMethods, natives, interfacesText.ToString(), t.BaseType.FullName == "java.lang.Object" ? string.Empty : mProxyImplementation));
            }
            return ret.ToArray();
        }
		
		private static readonly string mProxyImplementation = @"
	long myGCHandle;
	public long getGCHandle() {{
		return myGCHandle;
	}}

	public void setGCHandle(long gcHandle) {{
		myGCHandle = gcHandle;
	}}

    /*
	@Override
	protected void finalize() throws Throwable {{
	    super.finalize();
	    MonoBridge.releaseGCHandle(myGCHandle);
	}}
	*/
";

        public string GetClassProxyName(Type type)
        {
            if (type.DeclaringType == null)
                return type.Name;
            return GetClassProxyName(type.DeclaringType) + "_" + type.Name;
        }

        private MethodInfo FindBaseForMethod(MethodInfo sub, Type currentSuper)
        {
            if (currentSuper == null)
                return FindBaseForMethod(sub, sub.DeclaringType.BaseType);
            MethodInfo sup = currentSuper.GetMethod(sub.Name, BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public,
                null, sub.GetParameters().Select(x => x.ParameterType).ToArray(), null);
            if (sup != null)
            {
                foreach (var attrib in currentSuper.GetCustomAttributes(false))
                {
                    if (attrib.GetType().FullName == "MonoJavaBridge.JavaClassAttribute")
                        return sup;
                }
            }
            return currentSuper.BaseType != null ? FindBaseForMethod(sub, currentSuper.BaseType) : null;
        }

        private MethodInfo FindInterfaceForMethod(MethodInfo sub)
        {
            foreach (Type t in sub.DeclaringType.GetInterfaces())
            {
                MethodInfo sup = t.GetMethod(sub.Name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public,
                    null, sub.GetParameters().Select(x => x.ParameterType).ToArray(), null);
                if (sup != null)
                {
                    foreach (var attrib in t.GetCustomAttributes(false))
                    {
                        if (attrib.GetType().FullName == "MonoJavaBridge.JavaInterfaceAttribute")
                            return sup;
                    }
                }
            }
            return null;
        }

        private string GetJType(Type type)
        {
            string value = string.Empty;
            if (type == typeof(bool))
                value = "Z";
            else if (type == typeof(byte))
                value = "B";
            else if (type == typeof(char))
                value = "C";
            else if (type == typeof(short))
                value = "S";
            else if (type == typeof(int))
                value = "I";
            else if (type == typeof(long))
                value = "J";
            else if (type == typeof(float))
                value = "F";
            else if (type == typeof(double))
                value = "D";
            else if (type == typeof(void))
                value = "V";
            else
                value = "L" + type.FullName + ";";
            if (type.IsArray)
                value = "[" + value;
            return value.Replace('.', '/').Replace('+', '$').Replace('_', '$');
        }

        private string GetJLangType(Type type)
        {
            int elementTypeIndex = type.FullName.IndexOf('[');
            string elementType = elementTypeIndex == -1 ? type.FullName : type.FullName.Substring(0, elementTypeIndex);
            string remainder = elementTypeIndex == -1 ? string.Empty : type.FullName.Substring(elementTypeIndex);
            string convert;
            switch (elementType)
            {
                case "System.Boolean":
                    convert = "boolean";
                    break;
                case "System.Byte":
                    convert = "byte";
                    break;
                case "System.Char":
                    convert = "char";
                    break;
                case "System.Int16":
                    convert = "short";
                    break;
                case "System.Int32":
                    convert = "int";
                    break;
                case "System.Int64":
                    convert = "long";
                    break;
                case "System.Single":
                    convert = "float";
                    break;
                case "System.Double":
                    convert = "double";
                    break;
                case "System.Void":
                    convert = "void";
                    break;
                default:
                    convert = elementType.Replace('+', '.').Replace('_', '.');
                    break;
            }
            return convert + remainder;
        }
    }
}

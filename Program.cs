using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Descrambler
{
    internal static class Program
    {
        public static bool IsDebugBuild
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
            //AppDomain.CurrentDomain.AssemblyLoad += CurrentDomain_AssemblyLoad;
            Application.ThreadException += Application_ThreadException;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            System.Diagnostics.Debug.WriteLine($"[INFO] Application.VisualStyleState: {Application.VisualStyleState}");
            ListReferencedAssemblies();
            Application.Run(new frmMain());
        }

        #region [Domain Events]
        static void CurrentDomain_FirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e) => LogToFile($"CurrentDomain.FirstChanceException: {(e.Exception != null ? e.Exception.Message : "NULL")}");
        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) => LogToFile($"CurrentDomain.UnhandledException: {(e.ExceptionObject as Exception).Message}");
        static void CurrentDomain_AssemblyLoad(object sender, AssemblyLoadEventArgs args) => LogToFile($"CurrentDomain.AssemblyLoad: {(args.LoadedAssembly != null ? args.LoadedAssembly.FullName : "NULL")}");
        static void Application_ThreadException(object sender, ThreadExceptionEventArgs e) => LogToFile($"ThreadException: {e.Exception.Message}");
        #endregion

        #region [Reflection Helpers]
        /// <summary>
        /// Returns the declaring type's namespace.
        /// </summary>
        public static string GetCurrentNamespace() => System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType?.Namespace;

        /// <summary>
        /// Returns the declaring type's full name.
        /// </summary>
        public static string GetCurrentFullName() => System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType?.Assembly.FullName;

        /// <summary>
        /// Returns the declaring type's assembly name.
        /// </summary>
        public static string GetCurrentAssemblyName() => System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;

        /// <summary>
        /// Returns the AssemblyVersion, not the FileVersion.
        /// </summary>
        public static Version GetCurrentAssemblyVersion() => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version();
        #endregion

        #region [Extras]
        static void ListReferencedAssemblies()
        {
            System.Diagnostics.Debug.WriteLine($"-----[All Referenced Assemblies]-----");
            Assembly assembly = Assembly.GetExecutingAssembly();
            AssemblyName main = assembly.GetName();
            System.Diagnostics.Debug.WriteLine($"Main Assembly: {main.Name}, Version: {main.Version}");
            IOrderedEnumerable<AssemblyName> names = assembly.GetReferencedAssemblies().OrderBy(o => o.Name);
            foreach (var sas in names)
            {
                System.Diagnostics.Debug.WriteLine($" Sub Assembly: {sas.Name}, Version: {sas.Version}");
            }
        }

        /// <summary>
        /// Simple logger using a <see cref="StreamWriter"/> and the current namespace for the file name.
        /// </summary>
        /// <param name="msg">text to write to file</param>
        public static void LogToFile(string msg)
        {
            try
            {
                string output = $"[{DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff tt")}] {msg}\r\n";
                using (StreamWriter sw = new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), $"{GetCurrentNamespace()}.log"), true, Encoding.UTF8))
                {
                    sw.Write(output);
                    sw.Flush();
                }
            }
            catch (Exception) { /* typically a permission or file lock issue */ }
        }
        #endregion
    }
}

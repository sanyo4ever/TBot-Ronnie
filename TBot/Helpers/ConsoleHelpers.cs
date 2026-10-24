using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tbot.Helpers {
	public static class ConsoleHelpers {
		public static void SetTitle(string content = "") {
			Console.Title = GetTitle(content);
			return;
		}

		public static string GetTitle(string content = "") {
			Assembly assembly = Assembly.GetExecutingAssembly();
			AssemblyName exeInfo = assembly.GetName();
			string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
				?? exeInfo.Version?.ToString() ?? "unknown";
			string info = $"{exeInfo.Name} v{version}";
			return (content != "") ? $"{content} - {info}" : info;
		}

		public static void PlayAlarm() {
			Console.Beep();
			Thread.Sleep(1000);
			Console.Beep();
			Thread.Sleep(1000);
			Console.Beep();
			return;
		}
	}
}

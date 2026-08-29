using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	static class DumpInitOnLoad {
		[MenuItem("ShitDesigner/Tools/Dump InitializeOnLoadMethod")]
		static void Dump() {
			var lines = TypeCache.GetMethodsWithAttribute<InitializeOnLoadMethodAttribute>()
				.Select(m => $"{m.DeclaringType.Assembly.GetName().Name,-40} {m.DeclaringType.FullName}.{m.Name}")
				.OrderBy(s => s);
			Debug.Log($"[InitializeOnLoadMethod] x{lines.Count()}\n" + string.Join("\n", lines));
		}
	}
}

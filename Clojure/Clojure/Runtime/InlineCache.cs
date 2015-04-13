using System;
using System.Collections.Generic;
using System.Reflection;

namespace clojure.lang
{
	// HACK inline caches should be local to IFn instances, like constants
	public class InlineCacheStore
	{
		public static Dictionary<int, InlineCache> InlineCaches = new Dictionary<int, InlineCache> (32);

		public static int RegisterCache (InlineCache cache)
		{
			int id = RT.nextID ();
			InlineCaches.Add (id, cache);
			return id;
		}

		public static InlineCache GetCache (int id)
		{
			return InlineCaches [id];
		}
	}

	public class InlineCache
	{
		string methodName;
		Type[] paramTypes;
		Dictionary<Type, MethodInfo> cache;

		public InlineCache (string methodName, Type[] paramTypes)
		{
			this.methodName = methodName;
			this.paramTypes = paramTypes;

			this.cache = new Dictionary<Type, MethodInfo> (32);
		}

		public object Invoke(object reciever, object[] parameters)
		{
			Console.WriteLine("## InlineCache Invoke: " + reciever + "/" + methodName + " " + paramTypes.ToString());
			Type recieverType = reciever.GetType() == typeof(Type) ? (Type)reciever : reciever.GetType ();
			MethodInfo method;
			if (!cache.TryGetValue (recieverType, out method))
			{
				Console.WriteLine("   Cache Miss!");
				foreach (var m in recieverType.GetMethods())
				{
					if (m.Name == methodName)
					{
						Console.WriteLine ("   Found method named " + m.Name);
						ParameterInfo[] parameterInfo = m.GetParameters ();
						if (parameterInfo.Length == paramTypes.Length)
						{
							Console.WriteLine ("   Param lengths match");
							bool allTypesMatch = true;
							for (int i = 0; i < parameterInfo.Length; i++)
							{
								if (!parameterInfo [i].ParameterType.IsAssignableFrom (paramTypes [i])) {
									Console.WriteLine ("   Param " + i + "'s type " + parameterInfo [i].ParameterType + " does not match " + paramTypes [i]);
									allTypesMatch = false;
									break;
								} else {
									Console.WriteLine ("   Param " + i + "'s type " + parameterInfo [i].ParameterType + " matches " + paramTypes [i]);
								}
							}
							if (allTypesMatch)
							{
								Console.WriteLine ("   Method found! Updating cache");
								cache.Add (recieverType, m);
								method = m;
								break;
							}
						}
					}
				}
			}

			if (method == null)
				throw new Exception ("Could not find method " + methodName + " for reciever " + reciever);

			Console.WriteLine ("Invoking with");
			Console.WriteLine ("   reciever: " + reciever);
			for (int i = 0; i < parameters.Length; i++) {
				Console.WriteLine ("   param[" + i + "]: "  + parameters[i]);
			}


			return method.Invoke (reciever, parameters);
		}
	}
}
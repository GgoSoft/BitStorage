using GgoSoft.Storage;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace GgoSoft.Serialize
{
	internal static class DelegateParameterCache<T> where T : Delegate
	{
		// Statically cached array of argument types for this specific delegate type T
		public static readonly Type[] ArgumentTypes = GetArgumentTypes();

		private static Type[] GetArgumentTypes()
		{
			MethodInfo invokeMethod = typeof(T).GetMethod("Invoke")
				?? throw new InvalidOperationException($"{typeof(T).Name} is not a valid delegate.");

			ParameterInfo[] parameters = invokeMethod.GetParameters();

			if (parameters.Length == 0)
			{
				return [];
			}

			// Extract types, skipping the first parameter (the instance)
			int argCount = parameters.Length - 1;
			var argumentTypes = new Type[argCount];

			for (int i = 0; i < argCount; i++)
			{
				argumentTypes[i] = parameters[i + 1].ParameterType;
			}
			return argumentTypes;
		}
	}
	public static class MethodDelegateFactory
	{
		public delegate bool ShouldSerializeInvoker(object instance, BitStorage writer, object? data, PropertyInfo propInfo, int depth, int currentIndex);
		public delegate bool ShouldDeserializeInvoker(object instance, BitStorageReader reader, PropertyInfo propInfo, int depth, int currentIndex);
		public delegate bool ShouldContinueInvoker(object instance, BitStorageReader reader, PropertyInfo propInfo, int depth, int currentIndex);

		public static T? CreateMethod<T>(Type targetType, string? methodName) where T : Delegate
		{
			if(methodName == null)
			{
				return null;
			}
			Type[] parameterTypes = DelegateParameterCache<T>.ArgumentTypes;
			MethodInfo methodInfo = GetMethodOrThrow(targetType, methodName, parameterTypes);
			int paramCount = parameterTypes.Length;
			ParameterExpression[] parameters = new ParameterExpression[paramCount + 1];
			parameters[0] = Expression.Parameter(typeof(object));
			for (int i = 0; i < paramCount; i++)
			{
				parameters[i + 1] = Expression.Parameter(parameterTypes[i]);
			}

			MethodCallExpression methodCall;
			if (methodInfo.IsStatic)
			{
				methodCall = Expression.Call(null, methodInfo, parameters[1..]);
			}
			else
			{
				UnaryExpression instanceCast = Expression.Convert(parameters[0], targetType);
				methodCall = Expression.Call(instanceCast, methodInfo, parameters[1..]);
			}
			//var instanceCast = Expression.Convert(parameters[0], targetType);
			//var methodCall = Expression.Call(instanceCast, methodInfo, parameters[1..]);

			return Expression.Lambda<T>(methodCall, parameters).Compile();
		}

		private static MethodInfo GetMethodOrThrow(Type type, string name, Type[] paramTypes)
		{
			return type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, paramTypes)
				   ?? throw new InvalidOperationException($"Method {name} with matching signature not found on {type.Name}");
		}
		private static readonly ConcurrentDictionary<PropertyInfo, ShouldSerializeInvoker> _shouldSerializeCache = new();
		private static readonly ConcurrentDictionary<PropertyInfo, ShouldDeserializeInvoker> _shouldDeserializeCache = new();
		private static readonly ConcurrentDictionary<PropertyInfo, ShouldContinueInvoker> _shouldContinueCache = new();

		internal static ShouldSerializeInvoker GetShouldSerialize(PropertyInfo property, string methodName)
		{
			// No allocations or string manipulation are needed to check the cache
			return _shouldSerializeCache.GetOrAdd(property, prop =>
			{
				// Extract the declaring type straight from the property metadata
				Type targetType = prop.DeclaringType
					?? throw new InvalidOperationException("Property has no declaring type.");

				return CreateMethod<ShouldSerializeInvoker>(targetType, methodName);
			});
		}
		internal static ShouldDeserializeInvoker GetShouldDeserialize(PropertyInfo property, string methodName)
		{
			// No allocations or string manipulation are needed to check the cache
			return _shouldDeserializeCache.GetOrAdd(property, prop =>
			{
				// Extract the declaring type straight from the property metadata
				Type targetType = prop.DeclaringType
					?? throw new InvalidOperationException("Property has no declaring type.");

				return CreateMethod<ShouldDeserializeInvoker>(targetType, methodName);
			});
		}
		internal static ShouldContinueInvoker GetShouldContinue(PropertyInfo property, string methodName)
		{
			// No allocations or string manipulation are needed to check the cache
			return _shouldContinueCache.GetOrAdd(property, prop =>
			{
				// Extract the declaring type straight from the property metadata
				Type targetType = prop.DeclaringType
					?? throw new InvalidOperationException("Property has no declaring type.");

				return CreateMethod<ShouldContinueInvoker>(targetType, methodName);
			});
		}
	}
}

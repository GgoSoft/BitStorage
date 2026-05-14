using System;
using System.Linq;

namespace GgoSoft.Serialize
{
	/// <summary>
	/// Represents a set of delegates that provide dynamic getter and setter access to values on an object instance.
	/// </summary>
	/// <remarks>The Accessors struct enables flexible value retrieval and assignment for objects at runtime. It is
	/// commonly used in scenarios such as data binding, serialization, and reflection-based operations where property or
	/// field access needs to be determined dynamically. The struct also exposes information about the accessibility of the
	/// getter and setter, allowing consumers to make informed decisions when performing dynamic operations.</remarks>
	public struct Accessors
	{
		/// <summary>
		/// Gets the function used to retrieve a value from a specified object instance.
		/// </summary>
		/// <remarks>The getter function accepts an object as input and returns the corresponding value. This property
		/// is commonly used in scenarios that require dynamic value access, such as data binding or reflection-based
		/// operations.</remarks>
		public Func<object, object?> Getter { get; init; }
		/// <summary>
		/// Gets an action that assigns a value to a target object.
		/// </summary>
		/// <remarks>The action accepts the target object as the first parameter and the value to assign as the second
		/// parameter. This property enables custom logic for setting values, which can be useful in scenarios such as
		/// serialization, mapping, or dynamic property assignment.</remarks>
		public Action<object, object?> Setter { get; init; }
		/// <summary>
		/// Gets a value indicating whether the getter of the property is public.
		/// </summary>
		/// <remarks>This property is useful for determining the accessibility of the getter in scenarios where
		/// reflection or dynamic access is employed.</remarks>
		public bool GetterIsPublic { get; init; }
		public bool SetterIsPublic { get; init; }
	}
}

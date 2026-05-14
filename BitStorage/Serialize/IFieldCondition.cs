using System;
using System.Collections.Generic;
using System.Linq;

namespace GgoSoft.Serialize
{
	// -------------------------
	// Conditions
	// -------------------------
	public interface IFieldCondition
	{
		bool Evaluate(object instance,
					  IReadOnlyDictionary<string, object?> currentValues,
					  FieldMetadata field,
					  ConditionEvaluationMode mode,
					  SerializerContext context);
	}
}

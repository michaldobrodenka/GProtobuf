using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Helper class for finding and matching constructors for readonly struct deserialization.
    /// </summary>
    internal class ConstructorMatcher
    {
        public class FieldInfo
        {
            public int FieldId { get; set; }
            public string Name { get; set; }
            public ITypeSymbol Type { get; set; }
            public bool IsReadonly { get; set; }
        }

        public class ConstructorMatchResult
        {
            public IMethodSymbol? Constructor { get; set; }
            public List<ParameterMapping>? ParameterMappings { get; set; }
            public bool UseFormatterServices { get; set; }
            public bool UseParameterlessConstructor { get; set; }
            public string? ErrorMessage { get; set; }

            public bool IsSuccess => ErrorMessage == null;
        }

        public class ParameterMapping
        {
            public string ParameterName { get; set; }
            public int ParameterOrdinal { get; set; }
            public string FieldName { get; set; }
            public int FieldId { get; set; }
            public ITypeSymbol FieldType { get; set; }
        }

        /// <summary>
        /// Find the best constructor for deserializing a type with ProtoMember fields.
        /// </summary>
        public static ConstructorMatchResult FindBestConstructor(
            INamedTypeSymbol typeSymbol,
            List<FieldInfo> protoFields)
        {
            // Sort fields by fieldId (protobuf order)
            var sortedFields = protoFields.OrderBy(f => f.FieldId).ToList();

            // Check if any fields are readonly
            bool hasReadonlyFields = protoFields.Any(f => f.IsReadonly);

            // Try to find parameterless constructor
            var parameterlessConstructor = typeSymbol.Constructors
                .FirstOrDefault(c => c.Parameters.Length == 0
                                  && c.DeclaredAccessibility == Accessibility.Public
                                  && !c.IsStatic);

            // If no readonly fields, parameterless constructor is fine
            if (parameterlessConstructor != null && !hasReadonlyFields)
            {
                return new ConstructorMatchResult
                {
                    Constructor = parameterlessConstructor,
                    UseParameterlessConstructor = true,
                    ParameterMappings = new List<ParameterMapping>()
                };
            }

            // Try to find constructor matching all ProtoMember fields
            var matchedConstructor = FindMatchingConstructor(typeSymbol, sortedFields);

            if (matchedConstructor != null)
            {
                return matchedConstructor;
            }

            // Fallback strategies
            if (typeSymbol.TypeKind == TypeKind.Class)
            {
                // For classes without suitable constructor, use FormatterServices
                return new ConstructorMatchResult
                {
                    UseFormatterServices = true,
                    ParameterMappings = new List<ParameterMapping>()
                };
            }
            else if (typeSymbol.TypeKind == TypeKind.Struct)
            {
                if (hasReadonlyFields)
                {
                    return new ConstructorMatchResult
                    {
                        ErrorMessage = $"Cannot find suitable constructor for readonly struct '{typeSymbol.Name}'. " +
                                     $"Readonly struct requires a constructor with parameters matching all ProtoMember fields " +
                                     $"(by type and name, case-insensitive)."
                    };
                }
                else
                {
                    // Non-readonly struct without parameterless constructor
                    return new ConstructorMatchResult
                    {
                        ErrorMessage = $"Struct '{typeSymbol.Name}' has no parameterless constructor and no readonly fields. " +
                                     $"Add a parameterless constructor or make fields readonly with a matching constructor."
                    };
                }
            }

            return new ConstructorMatchResult
            {
                ErrorMessage = $"Cannot find suitable constructor for type '{typeSymbol.Name}'."
            };
        }

        /// <summary>
        /// Find constructor matching ProtoMember fields by type and name (case-insensitive).
        /// Implements protobuf-net "tuple-like type" matching with order-based fallback.
        /// </summary>
        private static ConstructorMatchResult? FindMatchingConstructor(
            INamedTypeSymbol typeSymbol,
            List<FieldInfo> protoFields)
        {
            foreach (var constructor in typeSymbol.Constructors)
            {
                if (constructor.DeclaredAccessibility != Accessibility.Public)
                    continue;

                if (constructor.IsStatic)
                    continue;

                // Constructor must have same number of parameters as ProtoMember fields
                if (constructor.Parameters.Length != protoFields.Count)
                    continue;

                // Strategy 1: Try to match by type AND name (case-insensitive)
                var mappings = TryMatchParameters(constructor, protoFields);

                if (mappings != null)
                {
                    return new ConstructorMatchResult
                    {
                        Constructor = constructor,
                        ParameterMappings = mappings,
                        UseParameterlessConstructor = false
                    };
                }

                // Strategy 2: Fallback to order-based matching (like protobuf-net does)
                mappings = TryMatchParametersByOrder(constructor, protoFields);

                if (mappings != null)
                {
                    return new ConstructorMatchResult
                    {
                        Constructor = constructor,
                        ParameterMappings = mappings,
                        UseParameterlessConstructor = false
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Try to match constructor parameters to ProtoMember fields.
        /// Returns mappings if successful, null otherwise.
        /// </summary>
        private static List<ParameterMapping>? TryMatchParameters(
            IMethodSymbol constructor,
            List<FieldInfo> protoFields)
        {
            var mappings = new List<ParameterMapping>();
            var unmatchedFields = new HashSet<FieldInfo>(protoFields);

            // Try to match each parameter to a field
            foreach (var parameter in constructor.Parameters)
            {
                FieldInfo? matchedField = null;

                // Find field matching by type AND name (case-insensitive)
                foreach (var field in unmatchedFields)
                {
                    if (SymbolEqualityComparer.Default.Equals(field.Type, parameter.Type) &&
                        string.Equals(field.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedField = field;
                        break;
                    }
                }

                if (matchedField == null)
                {
                    // No match found for this parameter
                    return null;
                }

                mappings.Add(new ParameterMapping
                {
                    ParameterName = parameter.Name,
                    ParameterOrdinal = parameter.Ordinal,
                    FieldName = matchedField.Name,
                    FieldId = matchedField.FieldId,
                    FieldType = matchedField.Type
                });

                unmatchedFields.Remove(matchedField);
            }

            // All fields must be matched
            if (unmatchedFields.Count > 0)
            {
                return null;
            }

            return mappings;
        }

        /// <summary>
        /// Try to match constructor parameters to ProtoMember fields by order.
        /// This is a fallback strategy when type+name matching fails.
        /// Maps parameters in declaration order to fields sorted by fieldId.
        /// Returns mappings if types match, null otherwise.
        /// </summary>
        private static List<ParameterMapping>? TryMatchParametersByOrder(
            IMethodSymbol constructor,
            List<FieldInfo> protoFields)
        {
            var mappings = new List<ParameterMapping>();

            // Sort fields by fieldId (protobuf order)
            var sortedFields = protoFields.OrderBy(f => f.FieldId).ToList();

            // Match parameters in order: param[0] → field with lowest fieldId, param[1] → next field, etc.
            for (int i = 0; i < constructor.Parameters.Length; i++)
            {
                var parameter = constructor.Parameters[i];
                var field = sortedFields[i];

                // Types must match
                if (!SymbolEqualityComparer.Default.Equals(field.Type, parameter.Type))
                {
                    // Type mismatch - order-based matching fails
                    return null;
                }

                mappings.Add(new ParameterMapping
                {
                    ParameterName = parameter.Name,
                    ParameterOrdinal = parameter.Ordinal,
                    FieldName = field.Name,
                    FieldId = field.FieldId,
                    FieldType = field.Type
                });
            }

            return mappings;
        }
    }
}


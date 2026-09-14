namespace Braid.Attributes;

/// <summary>Indicates that a type is intentionally mutable and should not be flagged by immutability rules.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
internal sealed class MutableAttribute : Attribute;

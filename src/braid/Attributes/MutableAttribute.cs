namespace Braid.Attributes;

/// <summary>Indicates that a type is intentionally mutable and should not be flagged by immutability rules.</summary>
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
internal sealed class MutableAttribute : System.Attribute;

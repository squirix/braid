namespace Braid.Attributes;

/// <summary>Indicates that a type is immutable.</summary>
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
internal sealed class ImmutableAttribute : System.Attribute;

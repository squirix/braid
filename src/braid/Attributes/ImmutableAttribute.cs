namespace Braid.Attributes;

/// <summary>Indicates that a type is immutable.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
internal sealed class ImmutableAttribute : Attribute;

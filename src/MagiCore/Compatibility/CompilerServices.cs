#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit;

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
    {
        public string ParameterName { get; } = parameterName;
    }

    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute;

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
    {
        public string FeatureName { get; } = featureName;
        public bool IsOptional { get; init; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class DoesNotReturnAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, Inherited = false)]
    internal sealed class NotNullAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Constructor, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute;
}
#endif
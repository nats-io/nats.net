#if NETSTANDARD

#pragma warning disable SA1201
#pragma warning disable SA1403

namespace System.Runtime.CompilerServices
{
    internal class ExtensionAttribute : Attribute
    {
    }

    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        public string FeatureName { get; }

        public bool IsOptional { get; init; }

        public const string RefStructs = nameof(RefStructs);

        public const string RequiredMembers = nameof(RequiredMembers);
    }

    internal sealed class RequiredMemberAttribute : Attribute
    {
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    internal sealed class SetsRequiredMembersAttribute : Attribute
    {
    }
}

#endif

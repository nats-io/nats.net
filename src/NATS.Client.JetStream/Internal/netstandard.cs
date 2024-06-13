﻿#if NETSTANDARD2_0 || NETSTANDARD2_1

#pragma warning disable SA1201
#pragma warning disable SA1403

namespace System.Runtime.CompilerServices
{
    public class ExtensionAttribute : Attribute
    {
    }

    public sealed class CompilerFeatureRequiredAttribute : Attribute
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

namespace TOTP.Tests.Common;

[CollectionDefinition(NonParallel, DisableParallelization = true)]
public sealed class NonParallelCollectionDefinition
{
    public const string NonParallel = "NonParallel";
}

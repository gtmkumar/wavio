using Xunit;

namespace WaPlatform.IntegrationTests.Support;

/// <summary>
/// All integration tests share ONE <see cref="DatabaseFixture"/> (one container per test run, not
/// per test/class — starting postgres:16 and applying 13 migrations per test would be far too
/// slow). Deliberately a single collection, not one per test class: xunit v2's default
/// parallelization runs different COLLECTIONS in parallel with each other but all tests WITHIN one
/// collection sequentially — putting every test here (rather than splitting by feature area into
/// several collections that would then run concurrently against the same container) is what makes
/// "unique tenant GUIDs, no transactions/TRUNCATE" a safe isolation strategy (see
/// DatabaseFixture's doc comment).
/// </summary>
[CollectionDefinition("IntegrationTests")]
public sealed class IntegrationTestCollectionDefinition : ICollectionFixture<DatabaseFixture>;

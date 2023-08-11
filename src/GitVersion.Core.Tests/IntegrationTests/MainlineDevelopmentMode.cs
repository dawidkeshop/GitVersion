using GitVersion.Configuration;
using GitVersion.Core.Tests.Helpers;
using GitVersion.Extensions;
using GitVersion.VersionCalculation;
using LibGit2Sharp;

namespace GitVersion.Core.Tests.IntegrationTests;

public class MainlineDevelopmentMode : TestBase
{
    private static GitFlowConfigurationBuilder GetConfigurationBuilder() => GitFlowConfigurationBuilder.New
        .WithBranch("main", builder => builder.WithVersioningMode(VersioningMode.Mainline))
        .WithBranch("develop", builder => builder.WithVersioningMode(VersioningMode.Mainline))
        .WithBranch("feature", builder => builder.WithVersioningMode(VersioningMode.Mainline))
        .WithBranch("support", builder => builder.WithVersioningMode(VersioningMode.Mainline))
        .WithBranch("pull-request", builder => builder.WithVersioningMode(null));

    [Test]
    public void VerifyNonMainMainlineVersionIdenticalAsMain()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit("1");

        fixture.BranchTo("feature/foo", "foo");
        fixture.MakeACommit("2 +semver: major");
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo");

        fixture.AssertFullSemver("1.0.0", configuration);

        fixture.BranchTo("support/1.0", "support");

        fixture.AssertFullSemver("1.0.0", configuration);
    }

    [Test]
    public void MergedFeatureBranchesToMainImpliesRelease()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit("1");
        fixture.MakeATaggedCommit("1.0.0");

        fixture.BranchTo("feature/foo", "foo");
        fixture.MakeACommit("2");
        fixture.AssertFullSemver("1.0.1-foo.1", configuration);
        fixture.MakeACommit("2.1");
        fixture.AssertFullSemver("1.0.1-foo.2", configuration);
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo");

        fixture.AssertFullSemver("1.0.1", configuration);

        fixture.BranchTo("feature/foo2", "foo2");
        fixture.MakeACommit("3 +semver: minor");
        fixture.AssertFullSemver("1.1.0-foo2.1", configuration);
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo2");
        fixture.AssertFullSemver("1.1.0", configuration);

        fixture.BranchTo("feature/foo3", "foo3");
        fixture.MakeACommit("4");
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo3");
        fixture.SequenceDiagram.NoteOver("Merge message contains '+semver: minor'", MainBranch);
        var commit = fixture.Repository.Head.Tip;
        // Put semver increment in merge message
        fixture.Repository.Commit(commit.Message + " +semver: minor", commit.Author, commit.Committer, new CommitOptions { AmendPreviousCommit = true });
        fixture.AssertFullSemver("1.2.0", configuration);

        fixture.BranchTo("feature/foo4", "foo4");
        fixture.MakeACommit("5 +semver: major");
        fixture.AssertFullSemver("2.0.0-foo4.1", configuration);
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo4");
        fixture.AssertFullSemver("2.0.0", configuration);

        // We should evaluate any commits not included in merge commit calculations for direct commit/push or squash to merge commits
        fixture.MakeACommit("6 +semver: major");
        fixture.AssertFullSemver("3.0.0", configuration);
        fixture.MakeACommit("7 +semver: minor");
        fixture.AssertFullSemver("3.1.0", configuration);
        fixture.MakeACommit("8");
        fixture.AssertFullSemver("3.1.1", configuration);

        // Finally verify that the merge commits still function properly
        fixture.BranchTo("feature/foo5", "foo5");
        fixture.MakeACommit("9 +semver: minor");
        fixture.AssertFullSemver("3.2.0-foo5.1", configuration);
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo5");
        fixture.AssertFullSemver("3.2.0", configuration);

        // One more direct commit for good measure
        fixture.MakeACommit("10 +semver: minor");
        fixture.AssertFullSemver("3.3.0", configuration);
        // And we can commit without bumping semver
        fixture.MakeACommit("11 +semver: none");
        fixture.AssertFullSemver("3.3.0", configuration);
        Console.WriteLine(fixture.SequenceDiagram.GetDiagram());
    }

    [Test]
    public void VerifyPullRequestsActLikeContinuousDelivery()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit("1");
        fixture.MakeATaggedCommit("1.0.0");
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.0.1", configuration);

        fixture.BranchTo("feature/foo", "foo");
        fixture.AssertFullSemver("1.0.2-foo.0", configuration);
        fixture.MakeACommit();
        fixture.MakeACommit();
        fixture.Repository.CreatePullRequestRef("feature/foo", MainBranch, prNumber: 8, normalise: true);
        fixture.AssertFullSemver("1.0.2-PullRequest8.3", configuration);
    }

    [Test]
    public void SupportBranches()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit("1");
        fixture.MakeATaggedCommit("1.0.0");
        fixture.MakeACommit(); // 1.0.1
        fixture.MakeACommit(); // 1.0.2
        fixture.AssertFullSemver("1.0.2", configuration);

        fixture.BranchTo("support/1.0", "support10");
        fixture.AssertFullSemver("1.0.2", configuration);

        // Move main on
        fixture.Checkout(MainBranch);
        fixture.MakeACommit("+semver: major"); // 2.0.0 (on main)
        fixture.AssertFullSemver("2.0.0", configuration);

        // Continue on support/1.0
        fixture.Checkout("support/1.0");
        fixture.MakeACommit(); // 1.0.3
        fixture.MakeACommit(); // 1.0.4
        fixture.AssertFullSemver("1.0.4", configuration);
        fixture.BranchTo("feature/foo", "foo");
        fixture.AssertFullSemver("1.0.5-foo.0", configuration);
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.0.5-foo.1", configuration);
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.0.5-foo.2", configuration);
        fixture.Repository.CreatePullRequestRef("feature/foo", "support/1.0", prNumber: 7, normalise: true);
        fixture.AssertFullSemver("1.0.5-PullRequest7.3", configuration);
    }

    [Test]
    public void VerifyForwardMerge()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit("1");
        fixture.MakeATaggedCommit("1.0.0");
        fixture.MakeACommit(); // 1.0.1

        fixture.BranchTo("feature/foo", "foo");
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.0.2-foo.1", configuration);
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.0.2-foo.2", configuration);

        fixture.Checkout(MainBranch);
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.0.2", configuration);
        fixture.Checkout("feature/foo");
        // This may seem surprising, but this happens because we branched off mainline
        // and incremented. Mainline has then moved on. We do not follow mainline
        // in feature branches, you need to merge mainline in to get the mainline version
        fixture.AssertFullSemver("1.0.2-foo.2", configuration);
        fixture.MergeNoFF(MainBranch);
        fixture.AssertFullSemver("1.0.3-foo.3", configuration);
    }

    [Test]
    public void VerifySupportForwardMerge()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit("1");
        fixture.MakeATaggedCommit("1.0.0");
        fixture.MakeACommit(); // 1.0.1

        fixture.BranchTo("support/1.0", "support10");
        fixture.MakeACommit();
        fixture.MakeACommit();

        fixture.Checkout(MainBranch);
        fixture.MakeACommit("+semver: minor");
        fixture.AssertFullSemver("1.1.0", configuration);
        fixture.MergeNoFF("support/1.0");
        fixture.AssertFullSemver("1.1.1", configuration);
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.1.2", configuration);
        fixture.Checkout("support/1.0");
        fixture.AssertFullSemver("1.0.3", configuration);

        fixture.BranchTo("feature/foo", "foo");
        fixture.MakeACommit();
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.0.4-foo.2", configuration); // TODO This probably should be 1.0.5
    }

    [Test]
    public void VerifyDevelopTracksMainVersion()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit("1");
        fixture.MakeATaggedCommit("1.0.0");
        fixture.MakeACommit();

        // branching increments the version
        fixture.BranchTo("develop");
        fixture.AssertFullSemver("1.1.0-alpha.0", configuration);
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.1.0-alpha.1", configuration);

        // merging develop into main increments minor version on main
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("develop");
        fixture.AssertFullSemver("1.1.0", configuration);

        // a commit on develop before the merge still has the same version number
        fixture.Checkout("develop");
        fixture.AssertFullSemver("1.1.0-alpha.1", configuration);

        // moving on to further work on develop tracks main's version from the merge
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.2.0-alpha.1", configuration);

        // adding a commit to main increments patch
        fixture.Checkout(MainBranch);
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.1.1", configuration);

        // adding a commit to main doesn't change develop's version
        fixture.Checkout("develop");
        fixture.AssertFullSemver("1.2.0-alpha.1", configuration);
    }

    [Test]
    public void VerifyDevelopFeatureTracksMainVersion()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit("1");
        fixture.MakeATaggedCommit("1.0.0");
        fixture.MakeACommit();

        // branching increments the version
        fixture.BranchTo("develop");
        fixture.AssertFullSemver("1.1.0-alpha.0", configuration);
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.1.0-alpha.1", configuration);

        // merging develop into main increments minor version on main
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("develop");
        fixture.AssertFullSemver("1.1.0", configuration);

        // a commit on develop before the merge still has the same version number
        fixture.Checkout("develop");
        fixture.AssertFullSemver("1.1.0-alpha.1", configuration);

        // a branch from develop before the merge tracks the pre-merge version from main
        // (note: the commit on develop looks like a commit to this branch, thus the .1)
        fixture.BranchTo("feature/foo");
        fixture.AssertFullSemver("1.0.2-foo.1", configuration);

        // further work on the branch tracks the merged version from main
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.1.1-foo.1", configuration);

        // adding a commit to main increments patch
        fixture.Checkout(MainBranch);
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.1.1", configuration);

        // adding a commit to main doesn't change the feature's version
        fixture.Checkout("feature/foo");
        fixture.AssertFullSemver("1.1.1-foo.1", configuration);

        // merging the feature to develop increments develop
        fixture.Checkout("develop");
        fixture.MergeNoFF("feature/foo");
        fixture.AssertFullSemver("1.2.0-alpha.2", configuration);
    }

    [Test]
    public void VerifyMergingMainToFeatureDoesNotCauseBranchCommitsToIncrementVersion()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.MakeACommit($"first in {MainBranch}");

        fixture.BranchTo("feature/foo", "foo");
        fixture.MakeACommit("first in foo");

        fixture.Checkout(MainBranch);
        fixture.MakeACommit($"second in {MainBranch}");

        fixture.Checkout("feature/foo");
        fixture.MergeNoFF(MainBranch);
        fixture.MakeACommit("second in foo");

        fixture.Checkout(MainBranch);
        fixture.MakeATaggedCommit("1.0.0");

        fixture.MergeNoFF("feature/foo");
        fixture.AssertFullSemver("1.0.1", configuration);
    }

    [Test]
    public void VerifyMergingMainToFeatureDoesNotStopMainCommitsIncrementingVersion()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.MakeACommit($"first in {MainBranch}");

        fixture.BranchTo("feature/foo", "foo");
        fixture.MakeACommit("first in foo");

        fixture.Checkout(MainBranch);
        fixture.MakeATaggedCommit("1.0.0");
        fixture.MakeACommit($"third in {MainBranch}");

        fixture.Checkout("feature/foo");
        fixture.MergeNoFF(MainBranch);
        fixture.MakeACommit("second in foo");

        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo");
        fixture.AssertFullSemver("1.0.2", configuration);
    }

    [Test]
    public void VerifyIssue1154CanForwardMergeMainToFeatureBranch()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.MakeACommit();
        fixture.AssertFullSemver("0.0.1", configuration);
        fixture.BranchTo("feature/branch2");
        fixture.BranchTo("feature/branch1");
        fixture.MakeACommit();
        fixture.MakeACommit();

        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/branch1");
        fixture.AssertFullSemver("0.0.2", configuration);

        fixture.Checkout("feature/branch2");
        fixture.MakeACommit();
        fixture.MakeACommit();
        fixture.MakeACommit();
        fixture.MergeNoFF(MainBranch);

        fixture.AssertFullSemver("0.0.3-branch2.4", configuration);
    }

    [Test]
    public void VerifyMergingMainIntoAFeatureBranchWorksWithMultipleBranches()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.MakeACommit($"first in {MainBranch}");

        fixture.BranchTo("feature/foo", "foo");
        fixture.MakeACommit("first in foo");

        fixture.BranchTo("feature/bar", "bar");
        fixture.MakeACommit("first in bar");

        fixture.Checkout(MainBranch);
        fixture.MakeACommit($"second in {MainBranch}");

        fixture.Checkout("feature/foo");
        fixture.MergeNoFF(MainBranch);
        fixture.MakeACommit("second in foo");

        fixture.Checkout("feature/bar");
        fixture.MergeNoFF(MainBranch);
        fixture.MakeACommit("second in bar");

        fixture.Checkout(MainBranch);
        fixture.MakeATaggedCommit("1.0.0");

        fixture.MergeNoFF("feature/foo");
        fixture.MergeNoFF("feature/bar");
        fixture.AssertFullSemver("1.0.2", configuration);
    }

    [Test]
    public void MergingFeatureBranchThatIncrementsMinorNumberIncrementsMinorVersionOfMain()
    {
        var configuration = GetConfigurationBuilder()
            .WithBranch("feature", builder => builder
                .WithVersioningMode(VersioningMode.ContinuousDeployment)
                .WithIncrement(IncrementStrategy.Minor)
            )
            .Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.MakeACommit($"first in {MainBranch}");
        fixture.MakeATaggedCommit("1.0.0");
        fixture.AssertFullSemver("1.0.0", configuration);

        fixture.BranchTo("feature/foo", "foo");
        fixture.MakeACommit("first in foo");
        fixture.MakeACommit("second in foo");
        fixture.AssertFullSemver("1.1.0-foo.2", configuration);

        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo");
        fixture.AssertFullSemver("1.1.0", configuration);
    }

    [Test]
    public void VerifyIncrementConfigIsHonoured()
    {
        var minorIncrementConfig = GitFlowConfigurationBuilder.New
            .WithIncrement(IncrementStrategy.Minor)
            .WithBranch("main", builder => builder
                .WithVersioningMode(VersioningMode.Mainline)
                .WithIncrement(IncrementStrategy.Inherit)
            )
            .WithBranch("feature", builder => builder
                .WithVersioningMode(VersioningMode.Mainline)
                .WithIncrement(IncrementStrategy.Inherit)
            )
            .Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit("1");
        fixture.MakeATaggedCommit("1.0.0");

        fixture.BranchTo("feature/foo", "foo");
        fixture.MakeACommit("2");
        fixture.AssertFullSemver("1.1.0-foo.1", minorIncrementConfig);
        fixture.MakeACommit("2.1");
        fixture.AssertFullSemver("1.1.0-foo.2", minorIncrementConfig);
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo");

        fixture.AssertFullSemver("1.1.0", minorIncrementConfig);

        fixture.BranchTo("feature/foo2", "foo2");
        fixture.MakeACommit("3 +semver: patch");
        fixture.AssertFullSemver("1.1.1-foo2.1", minorIncrementConfig);
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo2");
        fixture.AssertFullSemver("1.1.1", minorIncrementConfig);

        fixture.BranchTo("feature/foo3", "foo3");
        fixture.MakeACommit("4");
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo3");
        fixture.SequenceDiagram.NoteOver("Merge message contains '+semver: patch'", MainBranch);
        var commit = fixture.Repository.Head.Tip;
        // Put semver increment in merge message
        fixture.Repository.Commit(commit.Message + " +semver: patch", commit.Author, commit.Committer, new CommitOptions { AmendPreviousCommit = true });
        fixture.AssertFullSemver("1.1.2", minorIncrementConfig);

        var configuration = GetConfigurationBuilder().Build();
        fixture.BranchTo("feature/foo4", "foo4");
        fixture.MakeACommit("5 +semver: major");
        fixture.AssertFullSemver("2.0.0-foo4.1", minorIncrementConfig);
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo4");
        fixture.AssertFullSemver("2.0.0", configuration);

        // We should evaluate any commits not included in merge commit calculations for direct commit/push or squash to merge commits
        fixture.MakeACommit("6 +semver: major");
        fixture.AssertFullSemver("3.0.0", minorIncrementConfig);
        fixture.MakeACommit("7");
        fixture.AssertFullSemver("3.1.0", minorIncrementConfig);
        fixture.MakeACommit("8 +semver: patch");
        fixture.AssertFullSemver("3.1.1", minorIncrementConfig);

        // Finally verify that the merge commits still function properly
        fixture.BranchTo("feature/foo5", "foo5");
        fixture.MakeACommit("9 +semver: patch");
        fixture.AssertFullSemver("3.1.2-foo5.1", minorIncrementConfig);
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("feature/foo5");
        fixture.AssertFullSemver("3.1.2", minorIncrementConfig);

        // One more direct commit for good measure
        fixture.MakeACommit("10 +semver: patch");
        fixture.AssertFullSemver("3.1.3", minorIncrementConfig);
        // And we can commit without bumping semver
        fixture.MakeACommit("11 +semver: none");
        fixture.AssertFullSemver("3.1.3", minorIncrementConfig);
        Console.WriteLine(fixture.SequenceDiagram.GetDiagram());
    }

    [Test]
    public void BranchWithoutMergeBaseMainlineBranchIsFound()
    {
        var configuration = GetConfigurationBuilder()
            .WithBranch("unknown", builder => builder.WithVersioningMode(VersioningMode.Mainline))
            .WithAssemblyFileVersioningScheme(AssemblyFileVersioningScheme.MajorMinorPatchTag)
            .Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit();
        fixture.AssertFullSemver("0.0.1", configuration);
        fixture.BranchTo("master");
        fixture.Repository.Branches.Remove(fixture.Repository.Branches["main"]);
        fixture.AssertFullSemver("0.0.1", configuration);
        fixture.Repository.MakeCommits(2);
        fixture.AssertFullSemver("0.0.3", configuration);
        fixture.BranchTo("issue-branch");
        fixture.Repository.MakeACommit();
        fixture.AssertFullSemver("0.0.4-issue-branch.1", configuration);
    }

    [Test]
    public void GivenARemoteGitRepositoryWithCommitsThenClonedLocalDevelopShouldMatchRemoteVersion()
    {
        var configuration = GetConfigurationBuilder().Build();

        using var fixture = new RemoteRepositoryFixture();
        fixture.AssertFullSemver("0.0.5", configuration); // RemoteRepositoryFixture creates 5 commits.
        fixture.BranchTo("develop");
        fixture.AssertFullSemver("0.1.0-alpha.0", configuration);
        fixture.Repository.DumpGraph();
        var local = fixture.CloneRepository();
        fixture.AssertFullSemver("0.1.0-alpha.0", configuration, repository: local.Repository);
        local.Repository.DumpGraph();
    }

    [Test]
    public void GivenNoMainThrowsWarning()
    {
        using var fixture = new EmptyRepositoryFixture();
        fixture.Repository.MakeACommit();
        fixture.Repository.MakeATaggedCommit("1.0.0");
        fixture.Repository.MakeACommit();
        fixture.BranchTo("develop");
        fixture.Repository.Branches.Remove("main");

        var exception = Assert.Throws<WarningException>(() => fixture.AssertFullSemver("1.1.0-alpha.1", GetConfigurationBuilder().Build()));
        exception.ShouldNotBeNull();
        exception.Message.ShouldMatch("No branches can be found matching the commit .* in the configured Mainline branches: main, support");
    }

    [TestCase("feat!: Break stuff +semver: none")]
    [TestCase("feat: Add stuff +semver: none")]
    [TestCase("fix: Fix stuff +semver: none")]
    public void NoBumpMessageTakesPrecedenceOverBumpMessage(string commitMessage)
    {
        // Same configuration as found here: https://gitversion.net/docs/reference/version-increments#conventional-commit-messages
        var conventionalCommitsConfig = GetConfigurationBuilder()
            .WithMajorVersionBumpMessage("^(build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test)(\\([\\w\\s-]*\\))?(!:|:.*\\n\\n((.+\\n)+\\n)?BREAKING CHANGE:\\s.+)")
            .WithMinorVersionBumpMessage("^(feat)(\\([\\w\\s-]*\\))?:")
            .WithPatchVersionBumpMessage("^(build|chore|ci|docs|fix|perf|refactor|revert|style|test)(\\([\\w\\s-]*\\))?:")
            .Build();

        using var fixture = new EmptyRepositoryFixture();
        fixture.MakeATaggedCommit("1.0.0");

        fixture.MakeACommit(commitMessage);

        fixture.AssertFullSemver("1.0.0", conventionalCommitsConfig);
    }

    [Test]
    public void VerifyIssue3644BumpsMajorBasedOnLastTagNotTheFullHistory()
    {
        /*
mode: Mainline
assembly-versioning-format: '{Major}.{Minor}.{Patch}'
assembly-file-versioning-format: '{Major}.{Minor}.{Patch}.{WeightedPreReleaseNumber ?? 0}'
branches:
  master:
    is-mainline: true
    increment: None
  major:
    regex: ^major[\/-]
    increment: Major
    source-branches: ['master']
  minor:
    regex: ^minor[\/-]
    increment: Minor
    source-branches: ['master', 'support']
  patch:
    regex: ^patch[\/-]
    increment: Patch
    source-branches: ['master', 'support']
  support:
    is-mainline: true
    regex: ^support[/-]
    tag: ''
    increment: None
    source-branches: ['master']
*/

        var configuration = GitFlowConfigurationBuilder.New
            .WithBranch(MainBranch, builder => builder
                .WithVersioningMode(VersioningMode.Mainline)
                .WithIncrement(IncrementStrategy.None)
                .WithIsMainline(true))
            .WithBranch("major", builder => builder
                .WithRegularExpression("^major[\\/-]")
                .WithIncrement(IncrementStrategy.Major)
                .WithSourceBranches(MainBranch))
            .WithBranch("minor", builder => builder
                .WithRegularExpression("^minor[\\/-]")
                .WithIncrement(IncrementStrategy.Minor)
                .WithSourceBranches(MainBranch))
            .WithBranch("patch", builder => builder
                .WithRegularExpression("^patch[\\/-]")
                .WithIncrement(IncrementStrategy.Patch)
                .WithSourceBranches(MainBranch))
            .WithBranch("support", builder => builder
                .WithVersioningMode(VersioningMode.Mainline)
                .WithIncrement(IncrementStrategy.None)
                .WithRegularExpression("^support[\\/-]")
                .WithIsMainline(true)
                .WithSourceBranches(MainBranch))
            .Build();

        // implement history from the issue
        using var fixture = new EmptyRepositoryFixture();
        fixture.MakeACommit("First");
        fixture.MakeACommit("New file added");
        fixture.ApplyTag("v1.0.0");
        fixture.MakeACommit("Merged PR 123: new feature");
        fixture.ApplyTag("v2.0.2");

        // three branches, three merges to main
        fixture.BranchTo("minor/new-feature-1");
        fixture.MakeACommit("New class added");
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("minor/new-feature-1");

        fixture.BranchTo("minor/new-feature-2");
        fixture.MakeACommit("New class added");
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("minor/new-feature-2");

        fixture.BranchTo("minor/new-feature-3");
        fixture.MakeACommit("New class added");
        fixture.Checkout(MainBranch);
        fixture.MergeNoFF("minor/new-feature-3");

        fixture.ApplyTag("v2.1.0");

        // minor branch squashed
        fixture.MakeACommit("Merged PR 127: squashed feature");
        fixture.ApplyTag("v2.6.0");

        // let's implement a breaking change
        fixture.BranchTo("major/breaking-change");
        fixture.MakeACommit("Breaking change implemented");

        fixture.AssertFullSemver("3.0.0-breaking-change.1", configuration);
    }
}

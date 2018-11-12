﻿using System;
using System.IO;
using System.Threading.Tasks;
using GitHub.Services;
using LibGit2Sharp;
using NUnit.Framework;

public class GitServiceIntegrationTests
{
    public class TheGetLatestPushedShaMethod : TestBaseClass
    {
        [Test]
        public async Task EmptyRepository_ReturnsNull()
        {
            using (var temp = new TempDirectory())
            {
                string expectSha;
                var dir = temp.Directory.FullName;
                using (var repo = new Repository(Repository.Init(dir)))
                {
                    expectSha = null;
                }

                var target = new GitService(new RepositoryFacade());

                var sha = await target.GetLatestPushedSha(dir).ConfigureAwait(false);

                Assert.That(sha, Is.EqualTo(expectSha));
            }
        }

        [Test]
        public async Task HeadAndRemoteOnSameCommit_ReturnCommitSha()
        {
            using (var temp = new TempDirectory())
            {
                string expectSha;
                var dir = temp.Directory.FullName;
                using (var repo = new Repository(Repository.Init(dir)))
                {
                    AddCommit(repo); // First commit
                    var commit = AddCommit(repo);
                    expectSha = commit.Sha;
                    AddTrackedBranch(repo, repo.Head, commit);
                }

                var target = new GitService(new RepositoryFacade());

                var sha = await target.GetLatestPushedSha(dir).ConfigureAwait(false);

                Assert.That(sha, Is.EqualTo(expectSha));
            }
        }

        [Test]
        public async Task LocalAheadOfRemote_ReturnRemoteCommitSha()
        {
            using (var temp = new TempDirectory())
            {
                string expectSha;
                var dir = temp.Directory.FullName;
                using (var repo = new Repository(Repository.Init(dir)))
                {
                    AddCommit(repo); // First commit
                    var commit = AddCommit(repo);
                    expectSha = commit.Sha;
                    AddTrackedBranch(repo, repo.Head, commit);
                    AddCommit(repo);
                }

                var target = new GitService(new RepositoryFacade());

                var sha = await target.GetLatestPushedSha(dir).ConfigureAwait(false);

                Assert.That(sha, Is.EqualTo(expectSha));
            }
        }

        [Test]
        public async Task LocalBehindRemote_ReturnRemoteCommitSha()
        {
            using (var temp = new TempDirectory())
            {
                string expectSha;
                var dir = temp.Directory.FullName;
                using (var repo = new Repository(Repository.Init(dir)))
                {
                    AddCommit(repo); // First commit
                    var commit1 = AddCommit(repo);
                    var commit2 = AddCommit(repo);
                    repo.Reset(ResetMode.Hard, commit1);
                    expectSha = commit1.Sha;
                    AddTrackedBranch(repo, repo.Head, commit2);
                }

                var target = new GitService(new RepositoryFacade());

                var sha = await target.GetLatestPushedSha(dir).ConfigureAwait(false);

                Assert.That(sha, Is.EqualTo(expectSha));
            }
        }

        [Test]
        public async Task BranchForkedFromMaster_ReturnRemoteCommitSha()
        {
            using (var temp = new TempDirectory())
            {
                string expectSha;
                var dir = temp.Directory.FullName;
                using (var repo = new Repository(Repository.Init(dir)))
                {
                    AddCommit(repo); // First commit
                    var commit1 = AddCommit(repo);
                    AddTrackedBranch(repo, repo.Head, commit1);
                    var branch = repo.Branches.Add("branch", commit1);
                    Commands.Checkout(repo, branch);
                    var commit2 = AddCommit(repo);
                    expectSha = commit1.Sha;
                }

                var target = new GitService(new RepositoryFacade());

                var sha = await target.GetLatestPushedSha(dir).ConfigureAwait(false);

                Assert.That(sha, Is.EqualTo(expectSha));
            }
        }

        [Test]
        public async Task TowPossibleRemoteBranches_ReturnNearestCommitSha()
        {
            using (var temp = new TempDirectory())
            {
                string expectSha;
                var dir = temp.Directory.FullName;
                using (var repo = new Repository(Repository.Init(dir)))
                {
                    AddCommit(repo); // First commit
                    var commit1 = AddCommit(repo);
                    var commit2 = AddCommit(repo);
                    var commit3 = AddCommit(repo);
                    var branch1 = repo.Branches.Add("branch1", commit1);
                    AddTrackedBranch(repo, branch1, commit1);
                    var branch2 = repo.Branches.Add("branch2", commit2);
                    AddTrackedBranch(repo, branch2, commit2);
                    expectSha = commit2.Sha;
                }

                var target = new GitService(new RepositoryFacade());

                var sha = await target.GetLatestPushedSha(dir).ConfigureAwait(false);

                Assert.That(sha, Is.EqualTo(expectSha));
            }
        }

        [TestCase("origin", true)]
        [TestCase("jcansdale", true, Description = "Search all remotes")]
        public async Task BehindRemoteBranch_ReturnRemoteCommitSha(string remoteName, bool expectFound)
        {
            using (var temp = new TempDirectory())
            {
                string expectSha;
                var dir = temp.Directory.FullName;
                using (var repo = new Repository(Repository.Init(dir)))
                {
                    AddCommit(repo); // First commit
                    var commit1 = AddCommit(repo);
                    var commit2 = AddCommit(repo);
                    var branchA = repo.Branches.Add("branchA", commit2);
                    repo.Reset(ResetMode.Hard, commit1);
                    expectSha = expectFound ? commit1.Sha : null;
                    AddTrackedBranch(repo, branchA, commit2, remoteName: remoteName);
                }

                var target = new GitService(new RepositoryFacade());

                var sha = await target.GetLatestPushedSha(dir).ConfigureAwait(false);

                Assert.That(sha, Is.EqualTo(expectSha));
            }
        }

        static Commit AddCommit(Repository repo)
        {
            var dir = repo.Info.WorkingDirectory;
            var path = "file.txt";
            var file = Path.Combine(dir, path);
            var guidString = Guid.NewGuid().ToString();
            File.WriteAllText(file, guidString);
            Commands.Stage(repo, path);
            var signature = new Signature("foobar", "foobar@github.com", DateTime.Now);
            var commit = repo.Commit("message", signature, signature);
            return commit;
        }

        static void AddTrackedBranch(Repository repo, Branch branch, Commit commit,
            string trackedBranchName = null, string remoteName = "origin")
        {
            trackedBranchName = trackedBranchName ?? branch.FriendlyName;

            if (repo.Network.Remotes[remoteName] == null)
            {
                repo.Network.Remotes.Add(remoteName, "https://github.com/owner/repo");
            }
            var canonicalName = $"refs/remotes/{remoteName}/{trackedBranchName}";
            repo.Refs.Add(canonicalName, commit.Id);
            repo.Branches.Update(branch, b => b.TrackedBranch = canonicalName);
        }
    }
}
using KnowledgeBase.Agent.Wiki.Versioning;

namespace KnowledgeBase.Agent.Tests.Wiki.Versioning;

public sealed class WikiBackupSettingsTests
{
    [Test]
    public void AKeyDirectoryIsConfigured()
    {
        WikiBackupSettings settings = new() { Remote = "git@github.com:owner/wiki-backup.git", KeyDirectory = "/run/wiki-backup" };

        string? command = settings.SshCommand();

        ItShouldOfferOnlyTheDeployKeyAndTrustOnlyThePinnedHostKeys();

        void ItShouldOfferOnlyTheDeployKeyAndTrustOnlyThePinnedHostKeys() => command.ShouldBe(
            "ssh -i '/run/wiki-backup/id_ed25519' -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes "
            + "-o UserKnownHostsFile='/run/wiki-backup/known_hosts' -o BatchMode=yes");
    }

    [Test]
    public void TheKeyDirectoryHasAQuoteInItsName()
    {
        WikiBackupSettings settings = new() { KeyDirectory = "/srv/it's-keys" };

        string? command = settings.SshCommand();

        ItShouldKeepThePathOneShellWord();

        void ItShouldKeepThePathOneShellWord() =>
            command.ShouldStartWith("ssh -i '/srv/it'\\''s-keys/id_ed25519' ");
    }

    [Test]
    public void NoKeyDirectoryIsConfigured()
    {
        WikiBackupSettings settings = new() { Remote = "git@github.com:owner/wiki-backup.git" };

        string? command = settings.SshCommand();

        ItShouldLeaveSshAsItIs();

        void ItShouldLeaveSshAsItIs() => command.ShouldBeNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("  ")]
    public void NoRemoteIsConfigured(string? remote)
    {
        WikiBackupSettings settings = new() { Remote = remote, KeyDirectory = "/run/wiki-backup" };

        bool enabled = settings.IsEnabled;

        ItShouldBeOff();

        void ItShouldBeOff() => enabled.ShouldBeFalse();
    }
}

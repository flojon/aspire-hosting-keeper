using KoalaSoft.Aspire.Hosting.Keeper;
using SecretsManager;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests.Fakes;

internal sealed class FakeKeeperSecretsClient : IKeeperSecretsClient
{
    private readonly Dictionary<string, KeeperRecord> _recordsByUid;

    public int CallCount { get; private set; }
    public List<string[]> RequestedUidBatches { get; } = new();

    public FakeKeeperSecretsClient(params KeeperRecord[] records)
    {
        _recordsByUid = records.ToDictionary(r => r.RecordUid);
    }

    public Task<KeeperSecrets> GetSecretsAsync(SecretsManagerOptions options, string[] uids, CancellationToken cancellationToken)
    {
        CallCount++;
        RequestedUidBatches.Add(uids);
        var found = uids.Where(_recordsByUid.ContainsKey).Select(u => _recordsByUid[u]).ToArray();
        return Task.FromResult(new KeeperSecrets(appData: null!, expiresOn: null, records: found));
    }

    public static KeeperRecord MakeRecord(string uid, params (string type, string value)[] fields)
    {
        var recordFields = fields.Select(f => new KeeperRecordField { type = f.type, value = new object[] { f.value } }).ToArray();
        var data = new KeeperRecordData { title = uid, type = "login", fields = recordFields, custom = Array.Empty<KeeperRecordField>() };
        return new KeeperRecord(
            recordKey: Array.Empty<byte>(),
            recordUid: uid,
            folderUid: "folder",
            folderKey: Array.Empty<byte>(),
            innerFolderUid: "",
            data: data,
            revision: 1,
            files: Array.Empty<KeeperFile>(),
            links: Array.Empty<KeeperRecordLink>());
    }
}

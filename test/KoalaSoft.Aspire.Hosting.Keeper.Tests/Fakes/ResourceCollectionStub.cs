using Aspire.Hosting.ApplicationModel;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests.Fakes;

internal sealed class ResourceCollectionStub : List<IResource>, IResourceCollection
{
    public ResourceCollectionStub(params IResource[] resources) : base(resources) { }

    public bool TryGetByName(string name, out IResource? resource)
    {
        resource = this.FirstOrDefault(r => r.Name == name);
        return resource is not null;
    }
}

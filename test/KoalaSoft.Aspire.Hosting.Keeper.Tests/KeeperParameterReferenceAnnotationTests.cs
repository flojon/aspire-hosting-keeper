using Aspire.Hosting.ApplicationModel;
using KoalaSoft.Aspire.Hosting.Keeper;
using Xunit;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests;

public class KeeperParameterReferenceAnnotationTests
{
    [Fact]
    public void StoresNotation_AndIsAResourceAnnotation()
    {
        var annotation = new KeeperParameterReferenceAnnotation("keeper://UID/field/password");

        Assert.Equal("keeper://UID/field/password", annotation.Notation);
        Assert.IsAssignableFrom<IResourceAnnotation>(annotation);
    }
}

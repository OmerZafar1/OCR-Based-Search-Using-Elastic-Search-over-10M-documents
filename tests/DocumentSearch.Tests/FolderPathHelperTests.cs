using DocumentSearch.Infrastructure.Services;

namespace DocumentSearch.Tests;

public class FolderPathHelperTests
{
    [Fact]
    public void BuildMaterializedPath_RootFolder_ReturnsSlashWrappedPath()
    {
        var path = FolderPathHelper.BuildMaterializedPath("root", null);
        Assert.Equal("/root/", path);
    }

    [Fact]
    public void BuildMaterializedPath_ChildFolder_AppendsToParent()
    {
        var path = FolderPathHelper.BuildMaterializedPath("2024", "/contracts/");
        Assert.Equal("/contracts/2024/", path);
    }
}

using AdManager.Domain;
using AdManager.Domain.Enums;
using Xunit;

namespace AdManager.Domain.Tests;

public class SmokeTests
{
    [Fact]
    public void Role_holds_permissions()
    {
        var role = new HelpDeskRole { Name = "Хелпдеск L1" };
        role.Permissions.Add(Permission.ResetPassword);
        role.Permissions.Add(Permission.UnlockAccount);

        Assert.Contains(Permission.ResetPassword, role.Permissions);
        Assert.Equal(2, role.Permissions.Count);
    }

    [Fact]
    public void OperationResult_helpers_work()
    {
        Assert.True(OperationResult.Ok().Success);
        Assert.False(OperationResult.Fail("ошибка").Success);
    }
}

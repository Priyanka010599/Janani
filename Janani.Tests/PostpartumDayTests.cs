using Janani.Models;
using Xunit;

namespace Janani.Tests;

public class PostpartumDayTests
{
    // PregnancyStartDate far enough back that none of the DeliveryDate
    // values below (Today down to Today-200) are misread as "pre-pregnancy".
    private static UserProfile Profile(DateOnly? deliveryDate) => new()
    {
        PregnancyStartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-280),
        DeliveryDate = deliveryDate
    };

    [Fact]
    public void DeliveryDay_IsDayZero()
    {
        var profile = Profile(DateOnly.FromDateTime(DateTime.Today));
        Assert.Equal(0, profile.PostpartumDay);
    }

    [Fact]
    public void OneDayAfterDelivery_IsDayOne()
    {
        var profile = Profile(DateOnly.FromDateTime(DateTime.Today).AddDays(-1));
        Assert.Equal(1, profile.PostpartumDay);
    }

    [Fact]
    public void FortyTwoDaysAfterDelivery_IsDayFortyTwo()
    {
        var profile = Profile(DateOnly.FromDateTime(DateTime.Today).AddDays(-42));
        Assert.Equal(42, profile.PostpartumDay);
    }

    [Fact]
    public void TwoHundredDaysAfterDelivery_IsNotClampedAndReadsCorrectly()
    {
        var profile = Profile(DateOnly.FromDateTime(DateTime.Today).AddDays(-200));
        Assert.Equal(200, profile.PostpartumDay);
    }

    [Fact]
    public void NullDeliveryDate_ReturnsNull()
    {
        var profile = Profile(null);
        Assert.Null(profile.PostpartumDay);
    }

    [Fact]
    public void FutureDeliveryDate_ReturnsNull()
    {
        var profile = Profile(DateOnly.FromDateTime(DateTime.Today).AddDays(1));
        Assert.Null(profile.PostpartumDay);
    }

    [Fact]
    public void DeliveryDateBeforePregnancyStartDate_ReturnsNull()
    {
        var profile = new UserProfile
        {
            PregnancyStartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-10),
            DeliveryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-20)
        };
        Assert.Null(profile.PostpartumDay);
    }
}

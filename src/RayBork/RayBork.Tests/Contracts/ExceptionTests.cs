using RayBork.Internal;

namespace RayBork.Tests;

public sealed class ExceptionTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RayBorkExceptionPublicConstructorsLeaveDiagnosticCodeNull() {
        Assert.Null(new RayBorkException().DiagnosticCode);
        Assert.Null(new RayBorkException("boom").DiagnosticCode);
        Assert.Null(new RayBorkException("boom", new InvalidOperationException("inner")).DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GameExceptionPublicConstructorsLeaveDiagnosticCodeNull() {
        Assert.Null(new GameException().DiagnosticCode);
        Assert.Null(new GameException("boom").DiagnosticCode);
        Assert.Null(new GameException("boom", new InvalidOperationException("inner")).DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PlatformExceptionPublicConstructorsLeaveDiagnosticCodeNull() {
        Assert.Null(new PlatformException().DiagnosticCode);
        Assert.Null(new PlatformException("boom").DiagnosticCode);
        Assert.Null(new PlatformException("boom", new InvalidOperationException("inner")).DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WindowExceptionPublicConstructorsLeaveDiagnosticCodeNull() {
        Assert.Null(new WindowException().DiagnosticCode);
        Assert.Null(new WindowException("boom").DiagnosticCode);
        Assert.Null(new WindowException("boom", new InvalidOperationException("inner")).DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InternalCodeConstructorsSetDiagnosticCode() {
        Assert.Equal(
            DiagnosticCodes.GAME_CALLBACK_FAILED,
            new RayBorkException("boom", DiagnosticCodes.GAME_CALLBACK_FAILED, null).DiagnosticCode);
        Assert.Equal(
            DiagnosticCodes.GAME_CALLBACK_FAILED,
            new GameException("boom", DiagnosticCodes.GAME_CALLBACK_FAILED, null).DiagnosticCode);
        Assert.Equal(
            DiagnosticCodes.PLATFORM_INITIALIZATION_FAILED,
            new PlatformException("boom", DiagnosticCodes.PLATFORM_INITIALIZATION_FAILED, null).DiagnosticCode);
        Assert.Equal(
            DiagnosticCodes.WINDOW_CREATION_FAILED,
            new WindowException("boom", DiagnosticCodes.WINDOW_CREATION_FAILED, null).DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PublicInnerExceptionConstructorsPreserveInnerException() {
        InvalidOperationException inner = new("inner");

        Assert.Same(inner, new RayBorkException("boom", inner).InnerException);
        Assert.Same(inner, new GameException("boom", inner).InnerException);
        Assert.Same(inner, new PlatformException("boom", inner).InnerException);
        Assert.Same(inner, new WindowException("boom", inner).InnerException);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InternalCodeConstructorsPreserveInnerException() {
        InvalidOperationException inner = new("inner");

        Assert.Same(inner, new RayBorkException("boom", DiagnosticCodes.GAME_CALLBACK_FAILED, inner).InnerException);
        Assert.Same(inner, new GameException("boom", DiagnosticCodes.GAME_CALLBACK_FAILED, inner).InnerException);
        Assert.Same(
            inner,
            new PlatformException("boom", DiagnosticCodes.PLATFORM_INITIALIZATION_FAILED, inner).InnerException);
        Assert.Same(
            inner,
            new WindowException("boom", DiagnosticCodes.WINDOW_CREATION_FAILED, inner).InnerException);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HierarchyMatchesContract() {
        Assert.False(typeof(RayBorkException).IsSealed);
        Assert.True(typeof(RayBorkException).IsSubclassOf(typeof(Exception)));

        Type[] derivedTypes = [typeof(GameException), typeof(PlatformException), typeof(WindowException)];
        foreach (Type derivedType in derivedTypes) {
            Assert.True(derivedType.IsSealed, $"{derivedType.Name} must be sealed.");
            Assert.True(
                derivedType.IsSubclassOf(typeof(RayBorkException)),
                $"{derivedType.Name} must derive from RayBorkException.");
        }
    }
}

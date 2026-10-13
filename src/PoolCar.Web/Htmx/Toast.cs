using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace PoolCar.Web.Htmx;

public enum ToastKind
{
    Success,
    Error,
}

/// <summary>A short message that disappears after a few seconds.</summary>
public sealed record Toast(string Message, ToastKind Kind = ToastKind.Success)
{
    /// <summary>Renders a single toast.</summary>
    public const string Partial = "_Toast";

    /// <summary>Renders a toast that htmx appends to <c>#toasts</c>, next to the swapped content.</summary>
    public const string OutOfBandPartial = "_ToastOob";

    private const string MessageKey = "Toast.Message";
    private const string KindKey = "Toast.Kind";

    public static Toast Success(string message) => new(message, ToastKind.Success);

    public static Toast Error(string message) => new(message, ToastKind.Error);

    /// <summary>Keeps the toast for the next page, for example after a redirect.</summary>
    public void Store(ITempDataDictionary tempData)
    {
        tempData[MessageKey] = Message;
        tempData[KindKey] = Kind.ToString();
    }

    /// <summary>Reads and removes a toast stored for this page.</summary>
    public static Toast? Take(ITempDataDictionary tempData)
    {
        if (tempData[MessageKey] is not string message)
        {
            return null;
        }

        var kind = Enum.TryParse<ToastKind>(tempData[KindKey] as string, out var parsed) ? parsed : ToastKind.Success;
        return new Toast(message, kind);
    }
}

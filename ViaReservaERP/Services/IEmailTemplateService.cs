namespace ViaReservaERP.Services;

public interface IEmailTemplateService
{
    (string plainText, string html) GetForgotPasswordTemplate(string fullName, string resetLink);
    (string plainText, string html) GetReservationConfirmationTemplate(
        string guestName,
        string bookingId,
        string roomType,
        string checkIn,
        string checkOut,
        string amount,
        string paymentStatus);

    (string plainText, string html) GetSubscriptionWelcomeTemplate(
        string adminName,
        string companyName,
        string planName,
        string checkoutLink);

    (string plainText, string html) GetSubscriptionActiveTemplate(
        string adminName,
        string companyName,
        string planName,
        string loginLink);
}

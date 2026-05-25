using System.Text;

namespace ViaReservaERP.Services;

public class EmailTemplateService : IEmailTemplateService
{
    private const string PrimaryColor = "#0A192F"; // Deep Navy
    private const string GoldColor = "#C5A059";    // Luxury Gold
    private const string TextColor = "#333333";
    private const string LightGray = "#F4F7FA";

    public (string plainText, string html) GetForgotPasswordTemplate(string fullName, string resetLink)
    {
        var plainText = $@"Hi {fullName},

We received a request to reset your password for your ViaReserva account.
Click the link below to reset it:
{resetLink}

If you didn't request this, you can safely ignore this email.

Best regards,
The ViaReserva Team";

        var html = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: {TextColor}; margin: 0; padding: 0; background-color: {LightGray}; }}
                .container {{ max-width: 600px; margin: 20px auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
                .header {{ background-color: {PrimaryColor}; padding: 40px 20px; text-align: center; }}
                .header h1 {{ color: {GoldColor}; margin: 0; font-size: 24px; text-transform: uppercase; letter-spacing: 2px; }}
                .content {{ padding: 40px; }}
                .button-container {{ text-align: center; margin: 30px 0; }}
                .button {{ background-color: {GoldColor}; color: {PrimaryColor}; padding: 14px 28px; text-decoration: none; border-radius: 4px; font-weight: bold; display: inline-block; }}
                .footer {{ background-color: #eeeeee; padding: 20px; text-align: center; font-size: 12px; color: #777; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1>ViaReserva</h1>
                </div>
                <div class='content'>
                    <h2>Password Reset Request</h2>
                    <p>Hi {fullName},</p>
                    <p>We received a request to reset your password for your ViaReserva account. Click the button below to set a new password:</p>
                    <div class='button-container'>
                        <a href='{resetLink}' class='button'>Reset My Password</a>
                    </div>
                    <p>If the button doesn't work, copy and paste this link into your browser:</p>
                    <p style='word-break: break-all; color: #0066cc;'>{resetLink}</p>
                    <p>If you didn't request this, you can safely ignore this email. This link will expire in 1 hour.</p>
                </div>
                <div class='footer'>
                    &copy; {DateTime.Now.Year} ViaReserva ERP. All rights reserved.
                </div>
            </div>
        </body>
        </html>";

        return (plainText, html);
    }

    public (string plainText, string html) GetReservationConfirmationTemplate(
        string guestName,
        string bookingId,
        string roomType,
        string checkIn,
        string checkOut,
        string amount,
        string paymentStatus)
    {
        var plainText = $@"Hi {guestName},

Thank you for booking with ViaReserva! Your reservation is confirmed.

Booking Details:
- Booking ID: {bookingId}
- Room Type: {roomType}
- Check-in: {checkIn}
- Check-out: {checkOut}
- Total Amount: {amount}
- Payment Status: {paymentStatus}

We look forward to seeing you!

Best regards,
ViaReserva Team";

        var html = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: {TextColor}; margin: 0; padding: 0; background-color: {LightGray}; }}
                .container {{ max-width: 600px; margin: 20px auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
                .header {{ background-color: {PrimaryColor}; padding: 40px 20px; text-align: center; border-bottom: 4px solid {GoldColor}; }}
                .header h1 {{ color: {GoldColor}; margin: 0; font-size: 28px; text-transform: uppercase; letter-spacing: 3px; }}
                .content {{ padding: 40px; }}
                .booking-card {{ background-color: #f9f9f9; border: 1px solid #eee; border-radius: 8px; padding: 25px; margin: 25px 0; }}
                .detail-row {{ display: flex; justify-content: space-between; margin-bottom: 12px; border-bottom: 1px dashed #ddd; padding-bottom: 8px; }}
                .detail-label {{ font-weight: bold; color: #555; }}
                .detail-value {{ color: {PrimaryColor}; font-weight: 600; }}
                .footer {{ background-color: {PrimaryColor}; padding: 25px; text-align: center; font-size: 12px; color: #aaa; }}
                .status-badge {{ background-color: {GoldColor}; color: {PrimaryColor}; padding: 4px 10px; border-radius: 4px; font-size: 11px; font-weight: bold; text-transform: uppercase; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1>Reservation Confirmed</h1>
                </div>
                <div class='content'>
                    <h2 style='color: {PrimaryColor};'>Hello {guestName},</h2>
                    <p>Thank you for choosing ViaReserva. Your booking has been successfully processed and your room is reserved.</p>
                    
                    <div class='booking-card'>
                        <div class='detail-row'>
                            <span class='detail-label'>Booking ID</span>
                            <span class='detail-value'>#{bookingId}</span>
                        </div>
                        <div class='detail-row'>
                            <span class='detail-label'>Room Type</span>
                            <span class='detail-value'>{roomType}</span>
                        </div>
                        <div class='detail-row'>
                            <span class='detail-label'>Check-in</span>
                            <span class='detail-value'>{checkIn}</span>
                        </div>
                        <div class='detail-row'>
                            <span class='detail-label'>Check-out</span>
                            <span class='detail-value'>{checkOut}</span>
                        </div>
                        <div class='detail-row'>
                            <span class='detail-label'>Total Amount</span>
                            <span class='detail-value'>{amount}</span>
                        </div>
                        <div class='detail-row' style='border-bottom: none;'>
                            <span class='detail-label'>Payment Status</span>
                            <span class='status-badge'>{paymentStatus}</span>
                        </div>
                    </div>

                    <p>You can manage your reservation and check-in details through our Guest Portal.</p>
                </div>
                <div class='footer'>
                    &copy; {DateTime.Now.Year} ViaReserva ERP. Luxury Stay, Seamless Management.
                </div>
            </div>
        </body>
        </html>";

        return (plainText, html);
    }

    public (string plainText, string html) GetSubscriptionWelcomeTemplate(string adminName, string companyName, string planName, string checkoutLink)
    {
        var plainText = $@"Hi {adminName},

Welcome to ViaReserva! We're excited to have {companyName} on board.
Your signup for the {planName} plan is almost complete. Please finish your payment to activate your workspace:
{checkoutLink}

If you've already paid, you can ignore this message.

Best regards,
The ViaReserva Team";

        var html = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: {TextColor}; margin: 0; padding: 0; background-color: {LightGray}; }}
                .container {{ max-width: 600px; margin: 20px auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
                .header {{ background-color: {PrimaryColor}; padding: 40px 20px; text-align: center; border-bottom: 4px solid {GoldColor}; }}
                .header h1 {{ color: {GoldColor}; margin: 0; font-size: 28px; text-transform: uppercase; letter-spacing: 3px; }}
                .content {{ padding: 40px; }}
                .button-container {{ text-align: center; margin: 30px 0; }}
                .button {{ background-color: {GoldColor}; color: {PrimaryColor}; padding: 14px 28px; text-decoration: none; border-radius: 4px; font-weight: bold; display: inline-block; }}
                .footer {{ background-color: {PrimaryColor}; padding: 25px; text-align: center; font-size: 12px; color: #aaa; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1>Welcome to ViaReserva</h1>
                </div>
                <div class='content'>
                    <h2 style='color: {PrimaryColor};'>Hello {adminName},</h2>
                    <p>We're thrilled to have <strong>{companyName}</strong> join the ViaReserva family! You've chosen the <strong>{planName} Plan</strong>.</p>
                    <p>To get started and activate your enterprise workspace, please complete your subscription payment by clicking the button below:</p>
                    
                    <div class='button-container'>
                        <a href='{checkoutLink}' class='button'>Complete My Subscription</a>
                    </div>

                    <p>Once payment is confirmed, your account will be fully activated and you'll receive a follow-up email with your login details.</p>
                </div>
                <div class='footer'>
                    &copy; {DateTime.Now.Year} ViaReserva ERP. Enterprise Hotel Management.
                </div>
            </div>
        </body>
        </html>";

        return (plainText, html);
    }

    public (string plainText, string html) GetSubscriptionActiveTemplate(string adminName, string companyName, string planName, string loginLink)
    {
        var plainText = $@"Hi {adminName},

Great news! Your subscription for {companyName} ({planName} plan) is now ACTIVE.
You can now log in to your workspace and start managing your hotel:
{loginLink}

Welcome aboard!

Best regards,
The ViaReserva Team";

        var html = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: {TextColor}; margin: 0; padding: 0; background-color: {LightGray}; }}
                .container {{ max-width: 600px; margin: 20px auto; background: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
                .header {{ background-color: {PrimaryColor}; padding: 40px 20px; text-align: center; border-bottom: 4px solid {GoldColor}; }}
                .header h1 {{ color: {GoldColor}; margin: 0; font-size: 28px; text-transform: uppercase; letter-spacing: 3px; }}
                .content {{ padding: 40px; }}
                .button-container {{ text-align: center; margin: 30px 0; }}
                .button {{ background-color: {PrimaryColor}; color: {GoldColor}; border: 2px solid {GoldColor}; padding: 14px 28px; text-decoration: none; border-radius: 4px; font-weight: bold; display: inline-block; }}
                .footer {{ background-color: {PrimaryColor}; padding: 25px; text-align: center; font-size: 12px; color: #aaa; }}
                .success-badge {{ color: #22c55e; font-weight: 800; text-transform: uppercase; letter-spacing: 1px; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1>Workspace Activated</h1>
                </div>
                <div class='content'>
                    <div class='success-badge'>Subscription Active</div>
                    <h2 style='color: {PrimaryColor};'>Hello {adminName},</h2>
                    <p>Excellent news! The subscription for <strong>{companyName}</strong> has been successfully activated. Your <strong>{planName} Plan</strong> is now ready for use.</p>
                    <p>You can now log in to your enterprise dashboard to configure your rooms, rates, and start accepting reservations.</p>
                    
                    <div class='button-container'>
                        <a href='{loginLink}' class='button'>Log In to My Workspace</a>
                    </div>

                    <p>If you have any questions during your onboarding, our support team is here to help.</p>
                </div>
                <div class='footer'>
                    &copy; {DateTime.Now.Year} ViaReserva ERP. Luxury Stay, Seamless Management.
                </div>
            </div>
        </body>
        </html>";

        return (plainText, html);
    }
}

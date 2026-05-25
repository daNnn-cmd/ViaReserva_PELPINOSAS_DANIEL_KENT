# 🛎️ VíaReserva ERP — Enterprise-Grade Reservation & Management Platform

[![Framework](https://img.shields.io/badge/.NET-8.0-blueviolet.svg?style=flat-square&logo=.net)](https://dotnet.microsoft.com/download)
[![Database](https://img.shields.io/badge/Database-SQL%20Server%20%7C%20SQLite-blue.svg?style=flat-square&logo=microsoft-sql-server)](https://microsoft.com/sql-server)
[![Payments](https://img.shields.io/badge/Payments-Stripe-008cdd.svg?style=flat-square&logo=stripe)](https://stripe.com)
[![Email](https://img.shields.io/badge/Email-SendGrid-00b4db.svg?style=flat-square&logo=sendgrid)](https://sendgrid.com)

Welcome to **VíaReserva ERP**, a state-of-the-art, fully responsive, and secure Enterprise Resource Planning (ERP) platform designed for modern hospitality, lodging, and reservation management. Tailored for both enterprise administrators and front-desk staff, VíaReserva simplifies multi-tenant company onboarding, real-time workflow approvals, multi-layered auditing, and automated financial transaction pipelines.

---

## 🌟 Core Features & Modules

### 🏢 SuperAdmin Portal
An all-in-one administrative hub for enterprise governance:
* **Tenant & Company Management:** Onboard new companies, assign subscription tiers, archive inactive tenants, and manage enterprise inquiries.
* **Granular Role-Based Access Control (RBAC):** Create, update, and archive roles, managing feature access privileges system-wide.
* **Multi-Layered Auditing:** Real-time logging of system activity including:
  * User & Module Activity History
  * Reservation & Transaction Audit Logs
  * Security Incidents & Threat Logs
  * Subscription & Workflow Lifecycle Tracking
* **Workflow Management:** Design approval chains, track task durations, and manage department tasks.

### 🛎️ Front Desk & Reservations Operations
* **Multi-Step Walk-In Reservation Wizard:** Clean, step-by-step guest registration, room selection, and payment options.
* **Robust Form Validation:** Strict client-side validation for phone formats, email patterns, and mandatory guest details to ensure high-fidelity reservation data.
* **Service Requests:** Assign staff to reservation-specific services and monitor status updates live.

### 💳 Guest Portal & Payments
* **Responsive Portal:** Native mobile optimization with interactive navigation drawer and custom UI dashboard.
* **Stripe Payment Gateway:** Integrated payment processing utilizing Stripe Checkout, complete with webhook-driven reservation updates.
* **SendGrid Integration:** Automatic confirmation emails, invoice deliveries, and secure authentication messages.

### 📊 Enterprise Reporting & Charts
* **Audit-Auditable Reports:** Live export features for PDF (powered by `QuestPDF`), Excel (powered by `ClosedXML`), and CSV. All generated reports automatically inject administrative metadata (generating user and role details) into the headers for complete auditability.
* **Interactive Dashboards:** Dynamic visualizations using Radzen, ApexCharts, and Chart.js.

---

## 🛠️ Technology Stack

* **Backend Framework:** .NET 8.0 (ASP.NET Core MVC & Blazor Components)
* **ORM:** Entity Framework Core
* **Database Compatibility:** SQL Server (Production) / SQLite & LocalDB (Development)
* **Styling & UI:** Vanilla CSS, Tailwind CSS, Bootstrap, Radzen Blazor, and Syncfusion Components
* **External Integrations:** Stripe.net (Payments), Sendgrid (Emails)
* **Reporting Engines:** QuestPDF (PDF generation), ClosedXML (Excel spreadsheets)
* **Visualizations:** ApexCharts, Chart.js, Syncfusion Charts

---

## 🚀 Getting Started (Local Development)

Follow these steps to run the system in your local development environment:

### 📋 Prerequisites
* **.NET 8.0 SDK** or later
* **Visual Studio 2022** (with ASP.NET & Web Development workload)
* **LocalDB** (included in Visual Studio)

### 💻 Step-by-Step Installation

1. **Clone the Repository:**
   ```bash
   git clone https://github.com/daNnn-cmd/ViaReserva_PELPINOSAS_DANIEL_KENT.git
   cd ViaReserva_PELPINOSAS_DANIEL_KENT
   ```

2. **Restore NuGet Packages:**
   ```bash
   dotnet restore
   ```

3. **Configure Local Developer Secrets:**
   To protect sensitive keys, API credentials are **not** stored in `appsettings.json`. You must set up the **User Secrets Manager** locally on your machine.
   
   Run the following commands in your terminal inside the `ViaReservaERP` project folder to initialize and inject your own Stripe and SendGrid API Keys:
   ```bash
   # Add your Stripe Test Secret Key
   dotnet user-secrets set "Stripe:SecretKey" "sk_test_YOUR_STRIPE_SECRET_KEY"
   
   # Add your Stripe Webhook Secret (if using webhook testing)
   dotnet user-secrets set "Stripe:WebhookSecret" "whsec_YOUR_WEBHOOK_SECRET"

   # Add your SendGrid API Key
   dotnet user-secrets set "SendGrid:ApiKey" "SG.YOUR_SENDGRID_API_KEY"
   ```

4. **Initialize & Apply Database Migrations:**
   Create and update your database schema using EF Core:
   ```bash
   cd ViaReservaERP
   dotnet ef database update
   ```

5. **Run the Application:**
   ```bash
   dotnet run
   ```
   Open your browser and navigate to `https://localhost:7146` (or the HTTP port configured in your launch settings).

---

## 🔒 Secret Management Architecture

This codebase follows Microsoft's enterprise guidelines for secure development. Real credentials, tokens, and connection strings should **never** be committed to version control.

* **`appsettings.json`** contains developer placeholders (`YOUR_STRIPE_SECRET_KEY`, etc.) as safe fallback configuration keys.
* **`.gitignore`** is strictly configured to automatically block compiled assets (`bin/`, `obj/`), user settings (`*.user`), Local DB files (`*.db`), and temporary files.
* **Production Configurations** should be injected as Environment Variables (e.g. `STRIPE__SECRETKEY` and `SENDGRID__APIKEY`) on your web hosting environment (e.g. Azure, AWS, IIS).

---

## 📄 License & Terms

Developed as part of the **VíaReserva ERP** Enterprise platform. All rights reserved. For support, custom deployments, or license inquiry, please contact the administrator.

using System;

namespace ViaReservaERP.Services;

public interface ITaxService
{
    TaxResult CalculateTaxes(decimal baseAmount, int companyId);
}

public class TaxResult
{
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ServiceCharge { get; set; }
    public decimal Total { get; set; }
}

public class TaxService : ITaxService
{
    // In a real system, these rates would be fetched from the database per company.
    // For now, we'll use standard Philippine rates: 12% VAT and 10% Service Charge.
    private const decimal VatRate = 0.12m;
    private const decimal ServiceChargeRate = 0.10m;

    public TaxResult CalculateTaxes(decimal baseAmount, int companyId)
    {
        // Many businesses in the PH calculate service charge on the subtotal, 
        // then VAT on the (subtotal + service charge).
        
        var subtotal = baseAmount;
        var serviceCharge = Math.Round(subtotal * ServiceChargeRate, 2);
        var taxAmount = Math.Round((subtotal + serviceCharge) * VatRate, 2);
        var total = subtotal + serviceCharge + taxAmount;

        return new TaxResult
        {
            Subtotal = subtotal,
            ServiceCharge = serviceCharge,
            TaxAmount = taxAmount,
            Total = total
        };
    }
}

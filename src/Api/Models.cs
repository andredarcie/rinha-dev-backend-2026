using System.Text.Json.Serialization;

namespace RinhaBackend;

/// <summary>
/// Core transaction data: amount, installments, and the UTC timestamp of the request.
/// </summary>
public record TransactionInfo(
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("installments")] int Installments,
    [property: JsonPropertyName("requested_at")] string RequestedAt
);

/// <summary>
/// Customer behavioral profile used for anomaly detection: spending average, recent activity, and known merchants.
/// </summary>
public record CustomerInfo(
    [property: JsonPropertyName("avg_amount")] decimal AvgAmount,
    [property: JsonPropertyName("tx_count_24h")] int TxCount24h,
    [property: JsonPropertyName("known_merchants")] List<string> KnownMerchants
);

/// <summary>
/// Merchant context: identifier, Merchant Category Code (MCC), and the merchant's average transaction amount.
/// </summary>
public record MerchantInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("mcc")] string Mcc,
    [property: JsonPropertyName("avg_amount")] decimal AvgAmount
);

/// <summary>
/// Terminal and card-present flags, plus the physical distance from the customer's registered home address.
/// </summary>
public record TerminalInfo(
    [property: JsonPropertyName("is_online")] bool IsOnline,
    [property: JsonPropertyName("card_present")] bool CardPresent,
    [property: JsonPropertyName("km_from_home")] decimal KmFromHome
);

/// <summary>
/// Timestamp and location delta of the customer's immediately preceding transaction.
/// Null when no prior transaction exists for this customer.
/// </summary>
public record LastTransactionInfo(
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("km_from_current")] decimal KmFromCurrent
);

/// <summary>
/// Full payload for the <c>POST /fraud-score</c> endpoint, aggregating all context needed to score a transaction.
/// </summary>
public record FraudScoreRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("transaction")] TransactionInfo Transaction,
    [property: JsonPropertyName("customer")] CustomerInfo Customer,
    [property: JsonPropertyName("merchant")] MerchantInfo Merchant,
    [property: JsonPropertyName("terminal")] TerminalInfo Terminal,
    [property: JsonPropertyName("last_transaction")] LastTransactionInfo? LastTransaction
);

/// <summary>
/// Scoring result returned by <c>POST /fraud-score</c>.
/// <c>Approved</c> is <c>true</c> when <c>FraudScore</c> is below the approval threshold (0.6).
/// </summary>
public record FraudScoreResponse(
    [property: JsonPropertyName("approved")] bool Approved,
    [property: JsonPropertyName("fraud_score")] float FraudScore
);

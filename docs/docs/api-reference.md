# HTTP API Reference

## Health Check

```
GET /ready
```

Returns `200 OK` once the service has finished loading reference data and is ready to accept requests.
Use this as the Docker health check target.

---

## Score a Transaction

```
POST /fraud-score
Content-Type: application/json
```

### Request Body

```json
{
  "id": "txn-123",
  "transaction": {
    "amount": 250.00,
    "installments": 1,
    "requested_at": "2026-05-17T14:30:00Z"
  },
  "customer": {
    "avg_amount": 180.00,
    "tx_count_24h": 3,
    "known_merchants": ["merchant-abc"]
  },
  "merchant": {
    "id": "merchant-xyz",
    "mcc": "5411",
    "avg_amount": 120.00
  },
  "terminal": {
    "is_online": false,
    "card_present": true,
    "km_from_home": 2.5
  },
  "last_transaction": {
    "timestamp": "2026-05-17T13:00:00Z",
    "km_from_current": 1.2
  }
}
```

| Field | Type | Description |
|---|---|---|
| `id` | string | Unique transaction identifier |
| `transaction.amount` | decimal | Transaction value in local currency |
| `transaction.installments` | int | Number of installments (1 = single payment) |
| `transaction.requested_at` | string (ISO 8601) | UTC timestamp of the transaction |
| `customer.avg_amount` | decimal | Customer's historical average transaction amount |
| `customer.tx_count_24h` | int | Number of transactions by this customer in the last 24 hours |
| `customer.known_merchants` | string[] | List of merchant IDs previously transacted with |
| `merchant.id` | string | Merchant identifier |
| `merchant.mcc` | string | Merchant Category Code |
| `merchant.avg_amount` | decimal | Merchant's average transaction amount |
| `terminal.is_online` | bool | Whether the terminal is an online (card-not-present) channel |
| `terminal.card_present` | bool | Whether the physical card was presented |
| `terminal.km_from_home` | decimal | Distance in km from the customer's registered home address |
| `last_transaction` | object \| null | The customer's immediately preceding transaction; null if no history |
| `last_transaction.timestamp` | string (ISO 8601) | UTC timestamp of the prior transaction |
| `last_transaction.km_from_current` | decimal | Distance in km between the prior and current transaction locations |

### Response Body

```json
{
  "approved": true,
  "fraud_score": 0.2
}
```

| Field | Type | Description |
|---|---|---|
| `approved` | bool | `true` when `fraud_score < 0.6` |
| `fraud_score` | float | Fraction of the 5 nearest neighbors labeled as fraud (0.0–1.0) |

### Fraud Score Values

| Score | Meaning |
|---|---|
| 0.0 | All 5 neighbors are legitimate |
| 0.2 | 1 of 5 neighbors is fraud |
| 0.4 | 2 of 5 neighbors are fraud |
| 0.6 | 3 of 5 neighbors are fraud — **rejected** |
| 0.8 | 4 of 5 neighbors are fraud — **rejected** |
| 1.0 | All 5 neighbors are fraud — **rejected** |

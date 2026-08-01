# GamingServicesCloud

A pre-packaged solution for handling cloud-based gaming services in **Unity Cloud Code** modules.

This package provides ready-made **adapters** (handlers) for the most common Unity Gaming Services — **Cloud Save**, **Economy** (currencies & inventory) and **Store** (virtual + real-money purchases) — plus a **Player Profile** pipeline (get / load / init / sync) that you can call directly from your Cloud Code module.

---

## Features

- **Player Profile pipeline** — a single flow to get, load, initialize, and sync a player's profile
  (player data + economy data).
- **Cloud Save adapter** — generic key/value read & write through Unity Cloud Save.
- **Economy Currency adapter** — query, set, increment, and decrement player currency balances.
- **Economy Inventory adapter** — instance-based item management (add, update, delete, count).
- **Store adapter** — virtual purchases and real-money purchases (Google Play, Apple App Store, Fake store).
- **DTOs** — serialization-friendly payloads for player profile, economy data, and purchase results.
- Structured logging with `ILogger<T>` and descriptive error messages for every adapter.

---

## Installation

This package is meant to be used inside a **Unity Cloud Code** C# module. It depends on the official Unity SDK packages:

- `Unity.Services.CloudCode` (`Apis`, `Core`, `Shared`)
- `Unity.Services.CloudSave`
- `Unity.Services.Economy`
- `Newtonsoft.Json`
- `Microsoft.Extensions.Logging`

Add the `Adapters/` and `DTO/` folders to your Cloud Code module, then register the adapters in your dependency injection container.

```csharp
using GamingServicesCloud.Adapters;
using Microsoft.Extensions.DependencyInjection;

var builder = new ModuleBuilder();
builder.Services
    .AddSingleton<PlayerDataAdapter>()
    .AddSingleton<EconomyCurrencyAdapter>()
    .AddSingleton<EconomyInventoryAdapter>()
    .AddSingleton<StoreAdapter>()
    .AddSingleton<PlayerProfileAdapter>();
```

> `PlayerProfileAdapter` lives in the `DeployTestCloud` namespace in the sample. Rename/re-namespace it to match your module if needed.

---

## Package Structure

```
Adapters/
├── IPlayerProfileAdapter.cs        # Profile contract + BasePlayerProfileAdapter<T> template
├── PlayerProfileAdapter.cs         # Concrete Cloud Save + Economy profile implementation
├── PlayerDataAdapter.cs            # Cloud Save key/value operations
├── EconomyCurrencyAdapter.cs       # Currency balance operations
├── EconomyInventoryAdapter.cs      # Instance-based item operations
└── StoreAdapter.cs                 # Virtual + real-money purchases
DTO/
├── BasePlayerProfile.cs            # BasePlayerProfile<TPlayerData> response model
├── PlayerEconomyData.cs            # Currencies + item inventory snapshot
└── PurchaseResponse.cs             # PurchaseStatusCode enum + PurchaseResponse result
```

---

## Player Profile (get / load / init / sync)

### Contract — `IPlayerProfileAdapter`

| Method | Description |
|--------|-------------|
| `LoadPlayerProfile` | Loads an existing profile, or initializes a new one if none exists. Returns a `PlayerProfile` with `IsNewPlayer` set accordingly. |
| `TryGetPlayerData` | Safely attempts to fetch the player's `PlayerData`. Returns `(playerExists, playerData)`. |
| `InitializeNewPlayer` | Creates a default `PlayerData`, initializes the player's economy/inventory, and persists it. |
| `GetPlayerEconomyData` | Returns the player's current currencies and item inventory snapshot. |
| `SyncPlayerProfile` | Pushes client `PlayerData` (and optional economy data) back to the cloud and returns the latest profile. |

### Usage

```csharp
public async Task<PlayerProfile> OnGetPlayerProfile(IExecutionContext context, IGameApiClient gameApiClient)
{
    var adapter = context.Services.GetRequiredService<PlayerProfileAdapter>();

    // GET / LOAD
    var profile = await adapter.LoadPlayerProfile(context, gameApiClient);

    if (profile.IsNewPlayer)
    {
        // A brand-new player was initialized on the server.
    }

    // SYNC (client → server)
    var synced = await adapter.SyncPlayerProfile(
        context,
        gameApiClient,
        clientPlayerData: myClientData,
        clientEconomyData: myClientEconomy
    );

    return synced;
}
```

### How the flow works

```
LoadPlayerProfile
    ├─ TryGetPlayerData (Cloud Save)
    │    ├─ exists  → GetPlayerEconomyData (currencies + inventory) → PlayerProfile(IsNewPlayer = false)
    │    └─ missing → InitializeNewPlayer
    │                  ├─ InitializeInventory (hook, no-op by default)
    │                  ├─ SavePlayerDataWhenInit (Cloud Save)
    │                  └─ PlayerProfile(IsNewPlayer = true)

SyncPlayerProfile
    ├─ SavePlayerDataWhenSync (Cloud Save)
    ├─ SyncPlayerEconomyData (optional)
    │    ├─ Set absolute currency balances
    │    └─ Reconcile item instance counts (add missing / delete extras)
    └─ GetPlayerEconomyData → latest PlayerProfile
```

`BasePlayerProfileAdapter<T>` is an abstract template — implement the protected hooks
(`TryLoadPlayerDataWhenGet`, `SavePlayerDataWhenInit`, `InitializeInventory`, `GetPlayerEconomyData`,
`SavePlayerDataWhenSync`, `SyncPlayerEconomyData`) to customize storage and economy behavior.

---

## Cloud Save — `PlayerDataAdapter`

```csharp
await playerDataAdapter.SaveKey(context, gameApiClient, "PlayerData", new { DisplayName = "Alice" });

var (exists, value) = await playerDataAdapter.TryLoadKey(context, gameApiClient, "PlayerData");
if (exists)
{
    var data = JsonConvert.DeserializeObject<PlayerData>(value.ToString() ?? "{}");
}
```

| Method | Description |
|--------|-------------|
| `SaveKey` | Writes a single key/value via `CloudSaveData.SetItemAsync`. |
| `TryLoadKey` | Safely reads a key; returns `(false, null)` instead of throwing. |
| `LoadKey` | Reads a key and returns its raw value (throws on API failure). |

---

## Economy — `EconomyCurrencyAdapter`

```csharp
// Read
int coins = await currencyAdapter.GetCurrencyAmount(context, gameApiClient, "COINS");
var balances = await currencyAdapter.GetCurrenciesAmountMap(context, gameApiClient);

// Write
await currencyAdapter.SetCurrencyAmount(context, gameApiClient, "COINS", 500);
await currencyAdapter.IncrementCurrencyAmount(context, gameApiClient, "COINS", 100);
await currencyAdapter.DecrementCurrencyAmount(context, gameApiClient, "COINS", 50);
```

| Method | Description |
|--------|-------------|
| `GetCurrencyAmount` | Current balance of a currency (defaults to `0` if not found). |
| `GetCurrenciesAmountMap` / `GetCurrenciesAmountList` | All currency balances. |
| `SetCurrencyAmount` | Set an absolute balance. |
| `IncrementCurrencyAmount` | Add to the balance. |
| `DecrementCurrencyAmount` | Subtract from the balance. |

---

## Economy — `EconomyInventoryAdapter`

```csharp
// Count & check
int swords = await inventoryAdapter.GetItemCount(context, gameApiClient, "SWORD");
var map = await inventoryAdapter.GetItemsCountMap(context, gameApiClient);

// Add / update / delete
await inventoryAdapter.AddItem(context, gameApiClient, "SWORD");
await inventoryAdapter.AddItems(context, gameApiClient, "SWORD", 3);
await inventoryAdapter.UpdateItemData(context, gameApiClient, instanceId, new Dictionary<string, object> { ["level"] = 2 });
await inventoryAdapter.DeleteItem(context, gameApiClient, instanceId);
await inventoryAdapter.DeleteItems(context, gameApiClient, "SWORD", 2);

// Read custom instance data
int level = inventoryAdapter.GetInstanceCustomData<int>(instance, "level");
MyItemModel model = inventoryAdapter.GetParsedInstanceData<MyItemModel>(instance);
```

| Method | Description |
|--------|-------------|
| `HasItem` / `TryGetInventoryItemCount` / `GetItemCount` | Item ownership & count checks. |
| `GetItemsCountMap` / `GetItemCountList` | Counts grouped by item definition ID. |
| `AddItem` / `AddItems` | Grant one or many item instances. |
| `UpdateItemData` | Update a specific instance's custom data. |
| `DeleteItem` / `DeleteItems` | Remove one or many instances. |
| `GetItems` | Fetch raw inventory instances (optional filter by IDs / limit). |
| `GetInstanceCustomData` / `GetParsedInstanceData` | Extract custom data from an instance. |

---

## Store — `StoreAdapter`

### Virtual purchases

```csharp
var (status, message) = await storeAdapter.ProcessVirtualPurchase(context, gameApiClient, "vpp_sword_50");

if (status == PurchaseStatusCode.Success)
{
    // Rewards were applied automatically by the Economy service.
}
```

### Real-money purchases

`ProcessRealMoneyPurchase` validates the store receipt and redeems rewards. It supports the **Fake** store (testing), **Google Play**, and **Apple App Store**.

```csharp
var (status, message) = await storeAdapter.ProcessRealMoneyPurchase(
    context,
    gameApiClient,
    productId: "iap_sword",
    receipt: receiptJson,          // { "Store": "googleplay|appleappstore|fake", "Payload": "..." }
    localPrice: 0.99,
    currencyCode: "USD"
);
```

### Purchase result codes — `PurchaseStatusCode`

| Code | Meaning |
|------|---------|
| `Success` (0) | Purchase processed successfully. |
| `InvalidPurchaseId` (1000) / `InvalidRequest` (1001) / `Unauthorized` (1002) / `PlayerNotFound` (1003) / `InvalidReceipt` (1004) / `UnsupportedStore` (1005) | Client-side errors. |
| `InsufficientCurrency` (2000) / `InventoryFull` (2001) / `PurchaseLimitReached` (2002) / `PurchaseUnavailable` (2003) / `DuplicateTransaction` (2004) | Business/transaction errors. |
| `EconomyServiceUnavailable` (3000) / `ServerError` (3001) / `Timeout` (3002) / `Unknown` (9999) | Server errors. |

The adapter automatically maps Unity API HTTP status codes (`ApiException`) to these codes.

---

## DTOs

| Model | Description |
|-------|-------------|
| `BasePlayerProfile<TPlayerData>` | Wraps `PlayerData`, `EconomyData`, and `IsNewPlayer` (JSON-serializable). |
| `PlayerEconomyData` | Snapshot of `currencies` and `itemInventory` as dictionaries. |
| `PurchaseResponse` | Purchase result with `StatusCode`, `Success`, `Message`, and optional `EconomyData`. |

---

## Logging

Every adapter uses structured logging through `ILogger<T>` with consistent prefixes, e.g.:

- `[ProfileAdapter]` — profile pipeline events
- `[PlayerDataAdapter]` — Cloud Save events
- `[CurrencyAdapter]` / `[InventoryAdapter]` — economy events
- `[StoreAdapter]` — purchase events
- `[Business]` — successful business operations

---

## License

[MIT](LICENSE)

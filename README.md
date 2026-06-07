# 🏒 PuckDrop

> *We Know Puck All*

PuckDrop is an open-source game day poll app for friend groups. Create multiple-choice predictions before each game, lock in picks at puck drop, and track who's top of the leaderboard across the season. Built with Blazor WASM, AWS Lambda, and DynamoDB.

## How It Works

1. **Admin creates a poll** — multiple-choice questions for an upcoming game day
2. **Friends submit picks** — answer before the deadline (puck drop)
3. **Game happens** — voting locks at the deadline
4. **Admin scores** — marks correct answers post-game
5. **Leaderboard updates** — see who actually knows puck all

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Blazor WebAssembly |
| Backend | ASP.NET Core on AWS Lambda |
| Database | Amazon DynamoDB |
| Auth | Amazon Cognito |
| Infrastructure | AWS CDK / Aspire |
| Hosting | API Gateway (HTTP API v2) |

## Project Structure

```
PuckDrop/
├── API/src/
│   ├── PuckDrop.Api            # Lambda entry point, controllers
│   ├── PuckDrop.Application    # Use cases, business logic
│   ├── PuckDrop.Domain         # Entities, domain rules
│   └── PuckDrop.Infrastructure # DynamoDB repositories
├── UI/src/
│   └── PuckDrop.Web            # Blazor WASM frontend
├── Infrastructure/
│   ├── PuckDrop.AppHost        # Aspire orchestration
│   ├── PuckDrop.ServiceDefaults        # Shared server config
│   └── PuckDrop.ClientServiceDefaults  # Shared WASM config
└── docs/                       # Design documentation
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Aspire CLI](https://learn.microsoft.com/dotnet/aspire)
- AWS account (for deployment)

### Run Locally

```bash
aspire start
```

This starts the Aspire dashboard with the API (Lambda emulator), API Gateway emulator, DynamoDB Local, and the Blazor WASM frontend.

## Documentation

- [Domain Model](docs/domain-model.md)
- [API Design](docs/api-design.md)
- [DynamoDB Design](docs/dynamodb-design.md)
- [UI Pages](docs/ui-pages.md)

## License

MIT

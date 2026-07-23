# Build and Test

## Prerequisites

- Foundry `v0.1.0-alpha.7` or a compatible local development build
- Go with the `anvil` package tool available on `PATH`
- Task
- Python 3.12+ with the dependencies in `requirements.txt`
- `prek`, `ripgrep`, `zip`, and `unzip`

Set `FOUNDRY_BIN` when the Foundry executable is not on `PATH`:

```sh
export FOUNDRY_BIN=/path/to/foundry
```

## Commands

```sh
task lint
task test:foundry-script
task test:project
task test:ci
task test:package
task test
task package
```

`task test:project` installs the packages declared in
`test_project/packages.toml` with Anvil. The installed
`test_project/addons/foundrylib/` directory is generated and ignored by Git.

# Setup

## Requirements

### Unity

Use Unity `6000.0.62f1`

Install WebGL Build Support if you're working on the hosted watch app

### Runtime

macOS and Linux need:

- `bash`
- `python3`

Windows uses the same `arc.cmd` entrypoint

Docker is only needed for container testing

## Environment

The expected hosted env is:

```sh
ARCVIEWER_BASE_URL=https://watch.scoresaber.com/
ARCVIEWER_SCORESABER_BASE_URL=https://scoresaber.com/
ARCVIEWER_SCORESABER_API_URL=https://scoresaber.com/api/v2/
```

WebGL builds and local servers write runtime env to `Build/arcviewer-env.js`

If you want generated share links to point at your local server:

```sh
ARCVIEWER_BASE_URL=http://localhost:1183/
```

## Run Watch

Open the project in Unity Hub, or serve the last WebGL build:

```sh
./arc.cmd start
```

Open `http://localhost:1183`

Use a different port:

```sh
./arc.cmd start --port 8080
```

Rebuild before serving:

```sh
./arc.cmd start --rebuild
```

Windows:

```powershell
.\arc.cmd start
.\arc.cmd start -Port 8080
.\arc.cmd start -Rebuild
```

## Build

WebGL release:

```sh
./arc.cmd build webgl release
```

WebGL dev build:

```sh
./arc.cmd build webgl dev
```

Standalone builds:

```sh
./arc.cmd build macos release
./arc.cmd build linux64 release
./arc.cmd build win64 release
```

Windows:

```powershell
.\arc.cmd build webgl release
```

Build outputs go to `Build/`

## Docker

Build the image after a WebGL build:

```sh
docker build -t scoresaber-watch .
```

Run it:

```sh
docker run --rm -p 8080:8080 scoresaber-watch
```

Runtime URLs can be overridden:

```sh
docker run --rm -p 8080:8080 \
  -e ARCVIEWER_BASE_URL=https://watch.scoresaber.com/ \
  -e ARCVIEWER_SCORESABER_BASE_URL=https://scoresaber.com/ \
  -e ARCVIEWER_SCORESABER_API_URL=https://scoresaber.com/api/v2/ \
  scoresaber-watch
```

## Checks

For C# changes, open Unity and wait for compilation to finish

For WebGL, deployment, URL config or player only behavior, run one WebGL build when the change is ready:

```sh
./arc.cmd build webgl release
```

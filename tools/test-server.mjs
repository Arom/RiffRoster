import { spawn } from 'node:child_process';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

// Never reuse production data, keys, codes, VAPID credentials or an existing server.
const directory = mkdtempSync(join(tmpdir(), 'riffroster-e2e-'));
const root = resolve(import.meta.dirname, '..');
const project = process.env.RIFFROSTER_E2E_PROVIDER === 'Postgres' ? 'tests/RiffRoster.E2E/RiffRoster.E2E.csproj' : 'RiffRoster.csproj';
const child = spawn('dotnet', ['run', '--no-build', '--no-launch-profile', '--project', join(root, project)], {
  cwd: root, stdio: 'inherit', windowsHide: true,
  env: {...process.env,
    ASPNETCORE_ENVIRONMENT:'Development', DOTNET_ENVIRONMENT:'Development',
    ASPNETCORE_URLS:'http://127.0.0.1:5189',
    RIFFROSTER_STORAGE:'Json', RIFFROSTER_READ_ONLY:'false',
    RIFFROSTER_DATA_PATH:join(directory, 'store.json'), RIFFROSTER_BACKUP_DIR:join(directory, 'backups'),
    RIFFROSTER_KEYS_PATH:join(directory, 'keys'),
    RIFFROSTER_INVITE_CODE:'friends-local', RIFFROSTER_ADMIN_CODE:'admin-local-only',
    RIFFROSTER_VAPID_PUBLIC_KEY:'', RIFFROSTER_VAPID_PRIVATE_KEY:'', RIFFROSTER_VAPID_SUBJECT:''
  }
});
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal));
child.on('exit', code => { process.exitCode = code ?? 0; });
child.on('error', error => { console.error(error.message); process.exitCode = 1; });
// Leave isolated temporary state for failed-run inspection; OS temp cleanup may remove it.

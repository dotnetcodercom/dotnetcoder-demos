import json, os, subprocess
from pathlib import Path
from datetime import datetime, timezone

root = Path(__file__).resolve().parent
dotnet = os.environ.get('DNC_DOTNET', 'dotnet')
env = dict(os.environ, DOTNET_CLI_USE_MSBUILD_SERVER='0')
rows = []
for version in ['1.21.0', '1.22.0']:
    for scenario, args in [('cross-agent', []), ('normal', ['--normal']), ('injected-shared-control', ['--inject-shared'])]:
        command = [dotnet, 'run', '--configuration', 'Release', f'-p:AgentVersion={version}', '-p:NuGetAudit=false', '-p:UseSharedCompilation=false', '--', *args]
        result = subprocess.run(command, cwd=root, env=env, text=True, capture_output=True, timeout=90)
        expected = 'public=1; privileged=0' if scenario == 'normal' else 'public=0; privileged=' + ('1' if scenario == 'injected-shared-control' or version == '1.21.0' else '0')
        assert result.returncode == 0, result.stdout + result.stderr
        assert f'AgentAssembly={version}.0' in result.stdout, result.stdout
        assert 'Advertised=public_read\n' in result.stdout, result.stdout
        assert expected in result.stdout, result.stdout
        deps = json.loads((root/'bin/Release/net11.0/AgentToolProbe.deps.json').read_text())
        resolved = [x for x in deps['libraries'] if x.startswith('Microsoft.Extensions.AI/')]
        assert resolved == ['Microsoft.Extensions.AI/10.10.0'], resolved
        rows.append(dict(version=version, scenario=scenario, expected=expected, stdout=result.stdout, stderr=result.stderr, resolved_dependency=resolved[0], status='PASS'))
        print(version, scenario, 'PASS', flush=True)
(root/'verification-receipt.json').write_text(json.dumps(dict(status='PASS', checks=len(rows), verified_at=datetime.now(timezone.utc).isoformat(), rows=rows, scope='Exact article source, deterministic fake client, RunAsync only; no live provider.'), indent=2)+'\n')

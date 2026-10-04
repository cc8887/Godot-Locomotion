"""Compile and execute newly generated scalar/multiple-Pose ABI; preserves production assets."""
import hashlib
import argparse
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--tag', default='layer-codegen-v3')
tag = parser.parse_args().tag
assert re.fullmatch(r'[a-zA-Z0-9_-]+', tag)
OUT = ROOT / f'artifacts/lyra-analysis/{tag}-probe'
OUT.mkdir(exist_ok=False)
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()

def write(name, value):
    with (OUT / name).open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(value)

def run(name, args):
    with (OUT / name).open('x', encoding='utf-8') as log:
        completed = subprocess.run(args, cwd=ROOT.parent, stdout=log, stderr=subprocess.STDOUT, check=False)
    assert completed.returncode == 0, (name, completed.returncode)

def function(name, group, implemented, poses=(), parameters=()):
    return dict(name=name, group=group, implemented=implemented, inputPoses=list(poses),
                inputProperties=[dict(name=n, functionType=t, classType=t if implemented else '') for n, t in parameters],
                blendInTime=-1, blendOutTime=-1, blendInProfile='', blendOutProfile='')

def cls(name, functions, nodes=()):
    return dict(class_=name, skeleton='ProbeSkeleton', functions=functions, linkedNodes=list(nodes),
                receiveNotifies=False, propagateNotifies=False, useMainMontageData=False)

def call(node, name, interface, poses=()):
    return dict(node=node, layer=name, interface=interface, instanceClass='', inputPoses=list(poses),
                receiveNotifies=False, propagateNotifies=False)

parameters = [('Enabled', 'bool'), ('Counter', 'int32'), ('Ticks', 'int64'), ('Speed', 'float'), ('Heading', 'double')]
classes = dict(
    arbitrary_interface_a=cls('IfaceA', [function('IndependentMotionEvaluator', 'Declare', False, ('Primary', 'Secondary'), parameters),
                                        function('SharedRecoil', '', False)]),
    arbitrary_interface_b=cls('IfaceB', [function('ThirdInterfaceEntry', 'Other', False)]),
    owner=cls('Main', [function('AnimGraph', '', True)], [call('CallA', 'IndependentMotionEvaluator', 'IfaceA', ('Primary', 'Secondary')),
                                                       call('CallB', 'ThirdInterfaceEntry', 'IfaceB')]),
    implementation=cls('Provider', [function('IndependentMotionEvaluator', 'Implement', True, ('Primary', 'Secondary'), parameters),
                                    function('SharedRecoil', '', True)]))
for row in classes.values(): row['class'] = row.pop('class_')
write('inventory.json', '{}\n')
write('contracts.json', json.dumps(dict(schemaVersion=1, inventorySha256=sha(OUT / 'inventory.json'), classes=classes), indent=2) + '\n')
write('config.json', json.dumps(dict(interfaceClass='IfaceA', namespace='CodegenProbe', prefix='Probe', poseInputType='PoseInput')) + '\n')
write('ordinals.json', json.dumps(dict(IndependentMotionEvaluator=5, SharedRecoil=19)) + '\n')
cli = ROOT / 'tools/Als.LayerCodegen/bin/Release/net8.0/Als.LayerCodegen.dll'
arguments = [str(OUT / n) for n in ['contracts.json', 'config.json', 'ordinals.json', 'Generated.cs', 'inventory.json']]
run('generate.log', ['dotnet', str(cli), 'generate', *arguments])
run('check.log', ['dotnet', str(cli), 'check', *arguments])
write('Probe.csproj', f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>12</LangVersion><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
<ItemGroup><ProjectReference Include="{ROOT.as_posix()}/src/Als.Import/Als.Import.csproj" /></ItemGroup>
</Project>\n''')
write('Program.cs', '''using CodegenProbe;
using GodotAls.Core.Animation;
using GodotAls.Import;
var catalog = AlsAnimationLayerContractCompiler.Compile(File.ReadAllBytes(args[0]), File.ReadAllBytes(args[1]));
if(catalog.Classes.Length != 4) throw new Exception("Fixed class count");
catalog.ValidateImplementation("IfaceA", "Provider"); catalog.ValidateImplementation("IfaceB", "Provider");
ProbeGeneratedLayerContract.Validate(catalog.Class("IfaceA"));
var bindings = catalog.CreateBindings("Main"); bindings.Commit(bindings.PrepareLink("Provider"));
if(bindings.Target("CallA").Class != "Provider" || bindings.Target("CallA").Group != "Implement" || bindings.Target("CallB").Kind != AlsLinkedLayerTargetKind.Self) throw new Exception("Group/partial implementation");
var parameters = new ProbeIndependentMotionEvaluatorParameters(true, -12345, 1L << 40, 1.25f, 1.0000000000000002d);
if(!parameters.Enabled || parameters.Counter != -12345 || parameters.Ticks != 1L << 40 || parameters.Speed != 1.25f || BitConverter.DoubleToInt64Bits(parameters.Heading) != BitConverter.DoubleToInt64Bits(1.0000000000000002d)) throw new Exception("Scalar ABI loss");
var a = new PoseInput(new[]{1,2},new[]{.25f},new[]{1L<<40},1.0000000000000002d);
var b = new PoseInput(new[]{3,4},new[]{.75f},new[]{3L<<40},-2.5d);
var inputs = new ProbeIndependentMotionEvaluatorPoseInputs(a,b);
if(!inputs.Primary.Pose.SequenceEqual(a.Pose) || !inputs.Secondary.Pose.SequenceEqual(b.Pose) || !inputs.Primary.Curves.SequenceEqual(a.Curves) || !inputs.Secondary.Attributes.SequenceEqual(b.Attributes) || inputs.Primary.RootMotion != a.RootMotion || inputs.Secondary.RootMotion != b.RootMotion) throw new Exception("Pose payload loss");
if((int)ProbeLayerHook.IndependentMotionEvaluator != 5 || (int)ProbeLayerHook.SharedRecoil != 19) throw new Exception("Stable ID loss");
Console.WriteLine("ANIMATION_LAYER_GENERATED_ABI_OK classes=4 interfaces=2 functions=2 poses=2 scalarTypes=5 partial=true groupIndependent=true");
namespace CodegenProbe
{
internal readonly ref struct PoseInput
{
 internal readonly ReadOnlySpan<int> Pose; internal readonly ReadOnlySpan<float> Curves; internal readonly ReadOnlySpan<long> Attributes; internal readonly double RootMotion;
 internal PoseInput(ReadOnlySpan<int> pose,ReadOnlySpan<float> curves,ReadOnlySpan<long> attributes,double rootMotion){Pose=pose;Curves=curves;Attributes=attributes;RootMotion=rootMotion;}
}
}
''')
run('compile-and-run.log', ['dotnet', 'run', '--project', str(OUT / 'Probe.csproj'), '-c', 'Release', '--', str(OUT / 'contracts.json'), str(OUT / 'inventory.json')])
assert 'ANIMATION_LAYER_GENERATED_ABI_OK' in (OUT / 'compile-and-run.log').read_text(encoding='utf-8')
result = dict(passed=True, generatedFunctions=2, poseInputs=2, scalarTypes=5, newOriginalUeGraphAccepted=False,
              scriptSha256=sha(Path(__file__)), files={p.name: sha(p) for p in OUT.iterdir() if p.is_file()})
write('verification.json', json.dumps(result, indent=2) + '\n')
print(json.dumps(result))

"""Render the actual exported hybrid mesh data with Blender, without re-modeling.

Example (from the Unity project root):
  blender --background --python Tools/HybridRange/preview_blender.py -- \
    Build/Reports/AI-0.12.221-Hybrid/Current/world.json /absolute/output

The input is emitted by Tools/ProjectCheck/Hybrid221. Blender approximates the
range's fixed-direction shader with per-vertex lighting. These are geometry/art
review images, NOT Unity screenshots or evidence of device performance.
"""
import bpy
import base64
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
args = sys.argv[sys.argv.index('--') + 1:]
if len(args) not in (2, 3) or (len(args) == 3 and args[2] not in ('--wall-only', '--soldiers-only')):
    raise SystemExit('Expected geometry JSON and output directory, optionally --wall-only or --soldiers-only, after --')
wall_only = len(args) == 3 and args[2] == '--wall-only'
soldiers_only = len(args) == 3 and args[2] == '--soldiers-only'
source = Path(args[0]).resolve()
output = Path(args[1]).resolve()
output.mkdir(parents=True, exist_ok=True)
data = json.loads(source.read_text())

# Matching HybridMaterials.cs. Source images are the same full-resolution bytes.
SURFACES = {
    'Ground': ('surface-rough', (.66, .73, .58), (.24, .24), (0, 0, 1, 1)),
    'Road': ('surface-rough', (.69, .67, .61), (.45, .45), (0, 0, 1, 1)),
    'Concrete': ('surface-rough', (1.08, 1.08, 1.04), (.6, .6), (0, 0, 1, 1)),
    'Brick': ('surface-brick', (1.08, 1.05, 1.02), (.55, .36), (0, .055, 1, .89)),
    'Wood': ('surface-wood', (1.05, 1.02, .98), (.65, .5), (0, 0, 1, 1)),
    'Metal': ('surface-rough', (.57, .64, .60), (1.6, 1.6), (0, 0, 1, 1)),
    'Roof': ('surface-roof', (.93, .98, 1.0), (.45, .3), (0, 0, 1, 1)),
    'Glass': ('surface-rough', (.23, .31, .34), (.25, .25), (0, 0, 1, 1)),
}

def unity(v):
    return (v[0], -v[2], v[1])

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 8
scene.cycles.use_denoising = False
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.view_settings.exposure = 0
scene.view_settings.gamma = 1
scene.world.use_nodes = True
background = scene.world.node_tree.nodes.get('Background')
background.inputs['Color'].default_value = (.09, .115, .11, 1)
background.inputs['Strength'].default_value = 1

materials = {}
for surface, (name, tint, tile, rect) in SURFACES.items():
    mat = bpy.data.materials.new('Hybrid ' + surface)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()
    out = nodes.new('ShaderNodeOutputMaterial')
    emission = nodes.new('ShaderNodeEmission')
    emission.inputs['Strength'].default_value = 1
    links.new(emission.outputs['Emission'], out.inputs['Surface'])
    texture = nodes.new('ShaderNodeTexImage')
    texture.image = bpy.data.images.load(str(ROOT / 'Assets/Rubezh/Resources/HybridRange' / (name + '.png')), check_existing=True)
    texture.interpolation = 'Linear'
    texture.extension = 'REPEAT'
    uv = nodes.new('ShaderNodeTexCoord')
    scale = nodes.new('ShaderNodeVectorMath'); scale.operation = 'MULTIPLY'
    scale.inputs[1].default_value = (tile[0], tile[1], 1)
    links.new(uv.outputs['UV'], scale.inputs[0])
    repeat = nodes.new('ShaderNodeVectorMath'); repeat.operation = 'FRACTION'
    links.new(scale.outputs[0], repeat.inputs[0])
    crop = nodes.new('ShaderNodeVectorMath'); crop.operation = 'MULTIPLY_ADD'
    crop.inputs[1].default_value = (rect[2], rect[3], 1)
    crop.inputs[2].default_value = (rect[0], rect[1], 0)
    links.new(repeat.outputs[0], crop.inputs[0])
    links.new(crop.outputs[0], texture.inputs['Vector'])
    color = nodes.new('ShaderNodeVertexColor'); color.layer_name = 'hybrid-light'
    multiply = nodes.new('ShaderNodeMixRGB'); multiply.blend_type = 'MULTIPLY'
    multiply.inputs[0].default_value = 1
    links.new(texture.outputs['Color'], multiply.inputs[1])
    links.new(color.outputs['Color'], multiply.inputs[2])
    links.new(multiply.outputs[0], emission.inputs['Color'])
    materials[surface] = mat

# Actual soldier billboards use their original straight-alpha atlas, unlit.
actor_mat = bpy.data.materials.new('Hybrid original soldier sprites')
actor_mat.use_nodes = True
nodes = actor_mat.node_tree.nodes; links = actor_mat.node_tree.links
nodes.clear()
actor_out = nodes.new('ShaderNodeOutputMaterial')
actor_mix = nodes.new('ShaderNodeMixShader')
actor_transparent = nodes.new('ShaderNodeBsdfTransparent')
actor_emission = nodes.new('ShaderNodeEmission')
actor_texture = nodes.new('ShaderNodeTexImage')
actor_texture.image = bpy.data.images.load(str(ROOT / 'Assets/Rubezh/Resources/HybridRange/actors.png'), check_existing=True)
actor_texture.interpolation = 'Linear'
actor_texture.extension = 'CLIP'
links.new(actor_texture.outputs['Color'], actor_emission.inputs['Color'])
links.new(actor_texture.outputs['Alpha'], actor_mix.inputs[0])
links.new(actor_transparent.outputs[0], actor_mix.inputs[1])
links.new(actor_emission.outputs[0], actor_mix.inputs[2])
links.new(actor_mix.outputs[0], actor_out.inputs['Surface'])
materials['Actor'] = actor_mat

objects = []
for part in data['parts']:
    name = part['name']
    verts = part['vertices']
    indices = part['triangles']
    faces = [indices[i:i + 3] for i in range(0, len(indices), 3)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([unity(v) for v in verts], [], faces)
    mesh.update()
    ob = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(ob)
    surface = part['surface']
    mesh.materials.append(materials[surface])
    uv = mesh.uv_layers.new(name='UVMap')
    mesh.color_attributes.new(name='hybrid-light', type='FLOAT_COLOR', domain='CORNER')
    # Adding an attribute can invalidate an earlier RNA data-layer reference.
    uv = mesh.uv_layers.get('UVMap')
    colors = mesh.color_attributes.get('hybrid-light')
    normals = part['normals']
    tint = SURFACES[surface][1] if surface != 'Actor' else (1, 1, 1)
    for loop in mesh.loops:
        idx = loop.vertex_index
        uv.data[loop.index].uv = part['uv'][idx]
        n = normals[idx]
        key = max(0, min(1, n[0] * -.3904 + n[1] * .7809 + n[2] * .4880))
        sky = max(0, min(1, n[1] * .5 + .5))
        shade = .55 + .32 * key + .13 * sky if surface != 'Actor' else 1
        c = part['colors'][idx]
        if isinstance(c, str): c = base64.b64decode(c)
        colors.data[loop.index].color = tuple(c[k] / 255 * tint[k] * shade for k in range(3)) + (1,)
    ob['roof'] = part.get('roof', False)
    objects.append(ob)

cam_data = bpy.data.cameras.new('Hybrid fixed orthographic')
cam = bpy.data.objects.new('Hybrid fixed orthographic', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam_data.type = 'ORTHO'
cam_data.clip_start = .1
cam_data.clip_end = 180

# Unity orthographicSize measures half the vertical height. Blender's ortho_scale
# is horizontal when rendering landscape, so multiply by the output aspect ratio.
aspect = scene.render.resolution_x / scene.render.resolution_y

def render(name, target, size, roofs=True):
    direction = Vector((1, 1, 1)).normalized()
    eye = Vector(target) + direction * 60
    cam.location = unity(eye)
    look = Vector(unity(target)) - cam.location
    cam.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
    cam_data.ortho_scale = 2 * size * aspect
    for ob in objects:
        ob.hide_render = bool(ob['roof']) and not roofs
    scene.render.filepath = str(output / name)
    bpy.ops.render.render(write_still=True)

if soldiers_only:
    render('hybrid-with-soldiers.png', (18, 0, 12), 15)
elif not wall_only:
    render('hybrid-overview.png', (18, 0, 12), 15)
    render('hybrid-objects.png', (7.4, .8, 5.5), 4.8)
    render('hybrid-building-interior.png', (27.5, 1, 6.5), 4.8, roofs=False)
if not soldiers_only:
    render('hybrid-wall.png', (21, 1, 16.5), 3.7)
(output / ('soldier-preview-provenance.json' if soldiers_only else 'wall-preview-provenance.json' if wall_only else 'preview-provenance.json')).write_text(json.dumps({
    'source': str(source), 'renderer': bpy.app.version_string,
    'note': 'Offline review of actual exported Unity geometry and source PNGs; not a Unity screenshot.',
    'partCount': len(data['parts']),
    'triangles': sum(len(p['triangles']) // 3 for p in data['parts']),
}, ensure_ascii=False, indent=2) + '\n')
print('Hybrid mesh previews written to ' + str(output))

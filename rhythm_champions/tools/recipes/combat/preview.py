"""Private, disposable asset/choreography probe; run in Blender, not the game.

blender -b --python rhythm_champions/tools/recipes/combat/preview.py -- --out /tmp/combat-preview
Extracts only selected owned FBXs to OUT/sources. Never commit OUT: it contains vendor derivatives.
This tests rigid spectral motion and silhouette, not Unity shaders, retargeting or collision.
"""
import argparse
import math
import sys
import tarfile
from pathlib import Path

import bpy
from mathutils import Vector

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--out', type=Path, required=True)
parser.add_argument('--retarget-probe', action='store_true',
                    help='Diagnostic only: naive FBX rotation transfer; known to distort the knight')
parser.add_argument('--vendor', type=Path, default=Path(__file__).resolve().parents[3] / 'Assets/Vendor')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
args.out.mkdir(parents=True, exist_ok=True)
sources = args.out / 'sources'
sources.mkdir(exist_ok=True)
for package, guids in [
    ('POLYGON_Dungeon_Unity_2021_3_v1_9_5.unitypackage.gz', ['08c18a17006ad9a4eb9d96fe947f2af0']),
    ('POLYGON_BowAndCrossbow_Unity_2021_3_v1_0_1.unitypackage.gz',
     ['1c5b37fd30472d24fbd3e315d52fafaf', '2e3787f6a1057be42bdf7fb80d0d69b4']),
]:
    with tarfile.open(Path.home() / 'synty' / package) as archive:
        for guid in guids:
            name = Path(archive.extractfile(guid + '/pathname').read().decode()).name
            (sources / name).write_bytes(archive.extractfile(guid + '/asset').read())


def material(name, color):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Emission Color'].default_value = (*color, 1)
    bsdf.inputs['Emission Strength'].default_value = 1
    return mat


bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
white = material('hero edge', (0.8, 0.88, 1))
cyan = material('crowd edge', (0.015, 0.35, 0.65))
red = material('boss danger', (0.8, 0.015, 0.025))
dark = material('dark body', (0.005, 0.006, 0.01))
grid = material('quiet grid', (0.004, 0.02, 0.06))


def wire(obj, mat=white):
    obj.data.materials.clear()
    obj.data.materials.append(dark)
    obj.data.materials.append(mat)
    mod = obj.modifiers.new('closed facets', 'WIREFRAME')
    mod.thickness = 0.012
    mod.use_replace = False
    mod.material_offset = 1
    return obj


def line(points, mat=white, width=0.018):
    curve = bpy.data.curves.new('link', 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = width
    curve.bevel_resolution = 0
    spline = curve.splines.new('POLY')
    spline.points.add(len(points) - 1)
    for point, co in zip(spline.points, points):
        point.co = (*co, 1)
    obj = bpy.data.objects.new('link', curve)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def ring(x, y, r, mat=white):
    return line([(x + r * math.cos(i * math.tau / 24), y + r * math.sin(i * math.tau / 24), .06)
                 for i in range(25)], mat)


def load(path, target, length, name=None, axial=False, pose_time=None):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    added = set(bpy.data.objects) - before
    meshes = [o for o in added if o.type == 'MESH' and (name is None or o.name.startswith(name))]
    if pose_time is not None:
        rig = next(o for o in added if o.type == 'ARMATURE')
        clip = args.vendor / 'Synty/AnimationSwordCombat/Animations/Polygon/Attack/LightCombo01/A_Attack_LightCombo01A_Sword.fbx'
        before_clip = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(clip), use_anim=True)
        clip_objects = set(bpy.data.objects) - before_clip
        source_rig = next(o for o in clip_objects if o.type == 'ARMATURE')
        bpy.context.scene.frame_set(round(2 + pose_time * 30))
        bpy.context.view_layer.update()
        for bone in rig.pose.bones:
            source_bone = source_rig.pose.bones.get(bone.name)
            if source_bone:
                bone.rotation_mode = 'QUATERNION'
                bone.rotation_quaternion = source_bone.matrix_basis.to_quaternion()
        added |= clip_objects
    bpy.context.view_layer.update()
    # Freeze imported world coordinates; the optional naive transfer is a rejected diagnostic.
    verts, faces = [], []
    for obj in meshes:
        offset = len(verts)
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        verts.extend(obj.matrix_world @ v.co for v in mesh.vertices)
        faces.extend(tuple(offset + i for i in p.vertices) for p in mesh.polygons)
        evaluated.to_mesh_clear()
    for obj in added:
        bpy.data.objects.remove(obj, do_unlink=True)
    lo = Vector(tuple(min(v[i] for v in verts) for i in range(3)))
    hi = Vector(tuple(max(v[i] for v in verts) for i in range(3)))
    scale = length / max(hi - lo)
    data = bpy.data.meshes.new('private source mesh')
    centered = [(v - (lo + hi) / 2) * scale for v in verts]
    if axial:
        axis = max(range(3), key=lambda i: hi[i] - lo[i])
        rotation = Vector(tuple(1 if i == axis else 0 for i in range(3))).rotation_difference(Vector((1, 0, 0)))
        centered = [rotation @ v for v in centered]
        # Choose the thin end as handle, the thick end as the head/tip.
        low = [v for v in centered if v.x < -length * .25]
        high = [v for v in centered if v.x > length * .25]
        if sum(v.y ** 2 + v.z ** 2 for v in low) / max(1, len(low)) > sum(v.y ** 2 + v.z ** 2 for v in high) / max(1, len(high)):
            centered = [Vector((-v.x, v.y, -v.z)) for v in centered]
        centered = [v + Vector((length / 2, 0, 0)) for v in centered]
    data.from_pydata(centered, [], faces)
    obj = bpy.data.objects.new('spectral source', data)
    bpy.context.collection.objects.link(obj)
    obj.location = target
    wire(obj)
    return obj


scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 8
scene.render.resolution_x = 640
scene.render.resolution_y = 360
scene.render.resolution_percentage = 100
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.001, .001, .002, 1)
scene.view_settings.view_transform = 'Standard'
bpy.ops.object.camera_add(location=(9, -13, 17))
camera = bpy.context.object
camera.rotation_euler = (Vector((0, 1, 0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 18
scene.camera = camera
for i in range(-10, 11):
    line([(i, -10, 0), (i, 12, 0)], grid, .007)
    line([(-10, i, 0), (10, i, 0)], grid, .007)
for x, y in [(-4, 4), (-2, 5), (0, 5), (2, 5), (4, 4), (5, 2), (-5, 2)]:
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=.65, location=(x, y, .7))
    wire(bpy.context.object, cyan)
ring(0, 0, .45)
# A retained red threat crosses every preview: friendly effects must leave it visible.
line([(-7, 3, .08), (7, 3, .08)], red, .035)
base = set(bpy.data.objects)
knight = args.vendor / 'PolygonKnights/Models/Characters/Characters_POLYGON_Knights.fbx'
for label in ['hammer-swing', 'hammer-slam', 'bow-fan', 'lightning', 'field', 'sentinel']:
    for phase in range(5):
        for obj in set(bpy.data.objects) - base:
            bpy.data.objects.remove(obj, do_unlink=True)
        t = phase / 4
        if label.startswith('hammer'):
            obj = load(sources / 'SM_Wep_Hammer_Large_Metal_01.fbx', (0, 1, 1), 3, axial=True)
            if label == 'hammer-swing':
                obj.rotation_euler.z = math.radians(-100 + 200 * t)
                obj.location = (0, 1.4, 1)
            else:
                obj.rotation_euler.y = math.radians(-80 + 95 * t)
                obj.location.z = 1.2
                if t >= .75:
                    ring(2.9, 1, .5 + 5 * (t - .75))
        elif label == 'bow-fan':
            bow = load(sources / 'Rigged_Bow_Testing.fbx', (0, 0, 1.4), 1.8)
            bow.rotation_euler.z = math.pi / 2
            for angle in [-.4, -.2, 0, .2, .4]:
                x, y = math.sin(angle) * (1 + 6 * t), math.cos(angle) * (1 + 6 * t)
                arrow = load(sources / 'SM_Arrow_01.fbx', (x, y, 1), 1.1, axial=True)
                arrow.rotation_euler.z = math.pi / 2 - angle
                line([(x, y - .65, 1), (x, y, 1)], width=.012)
        elif label == 'lightning':
            for x in [-2, 0, 2]:
                line([(0, 0, 1), (x * .4, 1, 1.8), (x * .6 + .3, 1.7, 1),
                      (x, 2.5 + t * 2, .65)], width=.018 + .01 * (1 - t))
        elif label == 'field':
            ring(0, 1, 2.5)
            ring(0, 1, .7 + 1.8 * t)
            for angle in range(0, 360, 60):
                a = math.radians(angle)
                x, y = 1.5 * math.cos(a), 1 + 1.5 * math.sin(a)
                line([(x, y, .06), (x + .3, y + .2, .5 + .7 * math.sin(math.pi * t)), (x + .6, y, .06)])
        else:
            obj = load(knight, (1.5, 1 + 2 * t, .95), 1.85, 'Character_Knight_01', pose_time=.1 + .6 * t if args.retarget_probe else None)
            # Rest-pose source, or rejected naive-transfer diagnostic, plus root advance.
            # Use Unity RigPose for the actual sentinel, never this T-pose/transfer.
            ring(1.5, 1 + 2 * t, .6)
            a = t * math.pi
            line([(1.5, 1 + 2 * t, 1), (1.5 + 2 * math.cos(a), 1 + 2 * t + 2 * math.sin(a), 1)])
        scene.render.filepath = str(args.out / f'{label}-{phase}.png')
        bpy.ops.render.render(write_still=True)
print('Private probe frames:', args.out)

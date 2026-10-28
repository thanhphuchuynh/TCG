import importlib.util
import pathlib
import subprocess
import tempfile
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[1]
MONO=pathlib.Path('/Applications/Unity/Hub/Editor/2021.3.45f2/Unity.app/Contents/MonoBleedingEdge/bin')
spec=importlib.util.spec_from_file_location('prepare_surfaces',ROOT/'prepare_surfaces.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

def original_source():
    methods=[]
    def setters(variable, material):
        prefix='CSingleton<ShopCustomizationManager>.Instance.'+material
        return '\n'.join(prefix+'.SetTexture("'+slot+'", '+variable+'.'+field+');' for slot,field in [('_MainTex','mainTexture'),('_MetallicGlossMap','roughnessMap'),('_BumpMap','normalMap')])+ '\n'+prefix+'.SetFloat("_GlossMapScale", '+variable+'.smoothness);\n'+prefix+'.SetColor("_Color", '+variable+'.color);'
    for category in ['Floor','Wall','Ceiling']:
        variable=category.lower()+'DecoData'
        body='ShopDecoData '+variable+' = InventoryBase.Get'+category+'DecoData(index);\n'
        body+='if (isShopLotB) {'+setters(variable,'m_'+category+'MatB')+'} else {'+setters(variable,'m_'+category+'Mat')+'}\n'
        if category=='Wall':
            body+='if (wallDecoData.showBar) { ShopDecoData wallBarDecoData = InventoryBase.GetWallBarDecoData(index);\n'
            body+='if (isShopLotB) {'+setters('wallBarDecoData','m_WallBarMatB')+'} else {'+setters('wallBarDecoData','m_WallBarMat')+'} Trace.Bars++; }'
        methods.append('public static void Change'+category+'Material(int index, bool isShopLotB) { '+body+' }')
    return '''using System;using System.Collections.Generic;using System.Linq;
class Texture {} class GO {} class Material { public int Values; public void SetTexture(string s,Texture t){if(t==null)throw new Exception("lost override");Values++;} public void SetFloat(string s,float f){Trace.Floats++;} public void SetColor(string s,int c){Trace.Colors++;} }
class Trace {public static List<string> Keys=new List<string>();public static int Floats,Colors,Bars,Getters;}
class CSingleton<T>{public static T Instance;}
class ShopDecoData {public Texture mainTexture=new Texture(),roughnessMap=new Texture(),normalMap=new Texture();public float smoothness=0.7f;public int color=37;public bool showBar=true;}
class ShelfData {public List<ShopDecoData> m_FloorDecoDataList=new List<ShopDecoData>{new ShopDecoData()};public List<ShopDecoData> m_WallDecoDataList=new List<ShopDecoData>{new ShopDecoData()};public List<ShopDecoData> m_WallBarDecoDataList=new List<ShopDecoData>{new ShopDecoData()};public List<ShopDecoData> m_CeilingDecoDataList=new List<ShopDecoData>{new ShopDecoData()};}
class InventoryBase {public ShelfData m_ObjectData_SO=new ShelfData();static ShopDecoData Get(List<ShopDecoData> list,int i){Trace.Getters++;if(i>=list.Count)i=0;return list[i];}public static ShopDecoData GetFloorDecoData(int i){return Get(CSingleton<InventoryBase>.Instance.m_ObjectData_SO.m_FloorDecoDataList,i);}public static ShopDecoData GetWallDecoData(int i){return Get(CSingleton<InventoryBase>.Instance.m_ObjectData_SO.m_WallDecoDataList,i);}public static ShopDecoData GetWallBarDecoData(int i){return Get(CSingleton<InventoryBase>.Instance.m_ObjectData_SO.m_WallBarDecoDataList,i);}public static ShopDecoData GetCeilingDecoData(int i){return Get(CSingleton<InventoryBase>.Instance.m_ObjectData_SO.m_CeilingDecoDataList,i);}}
namespace TCG.SwitchStreaming {static class SurfaceTextures {public static void SetTexture(Material m,GO owner,string slot,string key,Texture fallback){Trace.Keys.Add(key);m.SetTexture(slot,fallback);}}}
class ShopCustomizationManager {public GO gameObject=new GO();public Material m_FloorMat=new Material(),m_FloorMatB=new Material(),m_WallMat=new Material(),m_WallMatB=new Material(),m_WallBarMat=new Material(),m_WallBarMatB=new Material(),m_CeilingMat=new Material(),m_CeilingMatB=new Material();\n'''+ '\n'.join(methods)+'''}
class Program {static void Main(){CSingleton<InventoryBase>.Instance=new InventoryBase();CSingleton<ShopCustomizationManager>.Instance=new ShopCustomizationManager();ShopCustomizationManager.ChangeFloorMaterial(0,false);ShopCustomizationManager.ChangeFloorMaterial(999,true);ShopCustomizationManager.ChangeWallMaterial(0,false);ShopCustomizationManager.ChangeWallMaterial(999,true);ShopCustomizationManager.ChangeCeilingMaterial(0,false);ShopCustomizationManager.ChangeCeilingMaterial(999,true);if(Trace.Keys.Count!=24||Trace.Keys.Any(k=>!k.Contains(":0:"))||Trace.Getters!=8||Trace.Floats!=8||Trace.Colors!=8||Trace.Bars!=2)throw new Exception("original flow/index changed");try{ShopCustomizationManager.ChangeFloorMaterial(-1,false);throw new Exception("negative silently fixed");}catch(ArgumentOutOfRangeException){}Console.Write("24:8:8:2");}}
'''

class SurfacePreparationTests(unittest.TestCase):
    def test_all_setters_preserve_actual_clamped_index_overrides_and_existing_flow(self):
        source=original_source();patched=module.patch_surfaces(source)
        self.assertEqual(module.patch_surfaces(patched),patched)
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory);(root/'test.cs').write_text(patched)
            subprocess.run([str(MONO/'mcs'),'-out:'+str(root/'test.exe'),str(root/'test.cs')],check=True,capture_output=True)
            self.assertEqual(subprocess.check_output([str(MONO/'mono'),str(root/'test.exe')],text=True),'24:8:8:2')
    def test_unknown_setter_or_method_rejected(self):
        with self.assertRaises(ValueError):module.patch_surfaces('class Unknown {}')
        with self.assertRaises(ValueError):module.patch_surfaces(original_source().replace('floorDecoData.mainTexture','floorDecoData.unknownTexture',1))
    def test_receipt_refuses_unrecorded_edits(self):
        with tempfile.TemporaryDirectory() as directory:
            project=pathlib.Path(directory)/'switch-on-demand/macos-port-work';source=project/'Assets/Scripts/Assembly-CSharp/ShopCustomizationManager.cs';source.parent.mkdir(parents=True);source.write_text(original_source())
            first=module.prepare(project);self.assertEqual(module.prepare(project),first)
            source.write_text(source.read_text()+'\n// unrelated edit\n')
            with self.assertRaises(ValueError):module.prepare(project)

    def test_backup_drift_is_refused_before_source_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            project=pathlib.Path(directory)/'switch-on-demand/macos-port-work';source=project/'Assets/Scripts/Assembly-CSharp/ShopCustomizationManager.cs';source.parent.mkdir(parents=True);source.write_text(original_source())
            receipt=module.prepare(project);saved=pathlib.Path(receipt['backup']);before=source.read_bytes()
            saved.write_text(saved.read_text().replace('public bool showBar=true','public bool showBar=false'))
            with self.assertRaises(ValueError):module.prepare(project)
            self.assertEqual(source.read_bytes(),before)
    def test_missing_receipt_never_blesses_prepared_or_interrupted_source(self):
        with tempfile.TemporaryDirectory() as directory:
            project=pathlib.Path(directory)/'switch-on-demand/macos-port-work';source=project/'Assets/Scripts/Assembly-CSharp/ShopCustomizationManager.cs';source.parent.mkdir(parents=True);source.write_text(original_source())
            module.prepare(project);before=source.read_bytes();(project/'surface-preparation.json').unlink()
            with self.assertRaises(ValueError):module.prepare(project)
            self.assertEqual(source.read_bytes(),before)
    def test_prepared_source_without_backup_or_receipt_is_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            project=pathlib.Path(directory)/'switch-on-demand/macos-port-work';source=project/'Assets/Scripts/Assembly-CSharp/ShopCustomizationManager.cs';source.parent.mkdir(parents=True);source.write_text(module.patch_surfaces(original_source()))
            before=source.read_bytes()
            with self.assertRaises(ValueError):module.prepare(project)
            self.assertEqual(source.read_bytes(),before)
    def test_intact_backup_before_first_write_can_recover(self):
        with tempfile.TemporaryDirectory() as directory:
            project=pathlib.Path(directory)/'switch-on-demand/macos-port-work';source=project/'Assets/Scripts/Assembly-CSharp/ShopCustomizationManager.cs';source.parent.mkdir(parents=True);source.write_text(original_source())
            saved=project.parent/'surface-source-backup/ShopCustomizationManager.cs';saved.parent.mkdir(parents=True);saved.write_bytes(source.read_bytes())
            result=module.prepare(project);self.assertEqual(module.prepare(project),result)

if __name__=='__main__':unittest.main()

import copy
import importlib.util
import pathlib
import unittest

spec=importlib.util.spec_from_file_location('body_validation',pathlib.Path(__file__).resolve().parents[1]/'validate_meshes.py')
validation=importlib.util.module_from_spec(spec);spec.loader.exec_module(validation)

class BodyCatalogueTests(unittest.TestCase):
    def reports(self):
        choices=[]
        for i in range(252):
            guid=format(i+1,'032x');choices.append({'guid':guid,'key':'mesh:'+guid,'path':'Assets/Choices/Part'+str(i)+'.asset','name':'part'+str(i),'bytes':100})
        bodies=[]
        for gender in ('Male','Female'):
            for lod in range(4):
                guid=format(1000+len(bodies),'032x');name=gender+'_Combined_LOD'+str(lod)
                bodies.append({'guid':guid,'key':'mesh:'+guid,'path':'Assets/Mesh/'+name+'.asset','name':name,'bytes':100})
        choice={'status':'prepared','eligible':choices,'bundleCount':252,'retained':[]}
        body={'status':'prepared','eligible':bodies,'bundleCount':8,'retained':[], 'sourceSceneUnchanged':True,
              'sceneBindingCount':224,'sceneOwnerCount':56,'prefabCount':6,'previousBundleCount':252,'previousBundlesUnchanged':True}
        return choice,body
    def test_complete_catalogue_proves_all_original_bodies_and_prior_choices(self):
        choice,body=self.reports();combined=validation.combine_reports(choice,body)
        manifest={'version':1,'entries':[{'key':r['key'],'bundle':'mesh_'+r['guid'],'asset':'assets/tcgstreamedmesh/'+r['guid']+'.asset','bytes':100,'crc':1} for r in choice['eligible']+body['eligible']]}
        self.assertEqual(len(validation.validate_catalog(combined,manifest)),260)
        self.assertEqual(len(choice['eligible']),252)
        for missing in (0,251,259):
            incomplete=copy.deepcopy(manifest);incomplete['entries'].pop(missing)
            with self.assertRaises(ValueError):validation.validate_catalog(combined,incomplete)
    def test_incomplete_or_unproved_scene_migration_refused(self):
        for field,value in [('status','started'),('bundleCount',7),('sceneBindingCount',220),('sceneOwnerCount',55),
                            ('prefabCount',5),('sourceSceneUnchanged',False),('previousBundleCount',251),('previousBundlesUnchanged',False)]:
            choice,body=self.reports();body[field]=value
            with self.subTest(field=field),self.assertRaises(ValueError):validation.combine_reports(choice,body)
        choice,body=self.reports();body['eligible'][0]['path']='Assets/Mesh/Replacement.asset'
        with self.assertRaises(ValueError):validation.combine_reports(choice,body)
        choice,body=self.reports();body['eligible'].pop()
        with self.assertRaises(ValueError):validation.combine_reports(choice,body)
        choice,body=self.reports();choice['eligible'].pop()
        with self.assertRaises(ValueError):validation.combine_reports(choice,body)
    def test_body_entries_require_separate_native_preparation_proof(self):
        choice,body=self.reports();choice['eligible']+=body['eligible'];choice['bundleCount']=260
        with self.assertRaises(ValueError):validation.combine_reports(choice,None)
    def test_body_leak_or_changed_nonbody_payload_is_rejected(self):
        choice,body=self.reports();records=validation.combine_reports(choice,body)['eligible']
        baseline={r['name']:['original-'+r['name']] for r in records};baseline['retained']=['original-retained']
        self.assertEqual(validation.resident_errors(baseline,{'retained':['original-retained']},records),[])
        self.assertTrue(validation.resident_errors(baseline,{'retained':['original-retained'], 'Male_Combined_LOD0':['original-Male_Combined_LOD0']},records))
        self.assertTrue(validation.resident_errors(baseline,{'retained':['changed']},records))

if __name__=='__main__':unittest.main()

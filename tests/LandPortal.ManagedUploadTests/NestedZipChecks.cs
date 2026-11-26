using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LandPortal.Infrastructure;
using OpenMcdf;
using UglyToad.PdfPig.Writer;

internal static class NestedZipChecks
{
    public static async Task Run(string[] args)
    {
        if(args.Length==3){await Real(args[1],args[2]);return;}
        var root=Path.Combine(Path.GetTempPath(),"LP-NestedZip-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        int count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine($"PASS {++count}: {name}");}
        byte[] Zip(params (string Name,byte[] Data)[] files){using var m=new MemoryStream();using(var z=new ZipArchive(m,ZipArchiveMode.Create,true))foreach(var f in files){using var s=z.CreateEntry(f.Name).Open();s.Write(f.Data);}return m.ToArray();}
        async Task<IReadOnlyList<EditorialZipPolicy.Entry>> Validate(byte[] b,bool trusted=true){var p=Path.Combine(root,Guid.NewGuid()+".zip");await File.WriteAllBytesAsync(p,b);return await EditorialZipPolicy.ValidateAsync(p,trustedInternal:trusted);}
        async Task Reject(byte[] b,string name,bool trusted=true){try{await Validate(b,trusted);}catch(Exception e)when(e is MediaUploadException or InvalidDataException){Check(true,name);return;}throw new Exception("Accepted "+name);}
        using var builder=new PdfDocumentBuilder();builder.AddPage(100,100);var pdf=builder.Build();
        var nested=Zip(("inner.zip",Zip(("doc.pdf",pdf),("table.xls",Xls()))));
        var inventory=await Validate(nested);Check(inventory.Count==3 && inventory.Any(x=>x.Name=="inner.zip!/table.xls"),"trusted one-level ZIP, real CFB/BIFF8 and SHA inventory");
        await Reject(nested,"ordinary ZIP policy still rejects nesting/XLS",false);
        await Reject(Zip(("a.zip",nested)),"two nested levels denied");
        await Reject(Zip(("a.pdf",pdf),("empty.zip",Zip(("empty/",Array.Empty<byte>())))),"nested directory-only archive denied even after a valid outer document");
        await Reject(Zip(("a.zip",Zip(("../x.pdf",pdf)))),"nested traversal denied");
        await Reject(Zip(("a.zip",Zip(("run.exe",pdf)))),"nested executable denied");
        await Reject(Zip(("a.xls",pdf)),"fake XLS denied");
        await Reject(Zip(("a.xls",Xls(macro:true))),"VBA storage denied");
        await Reject(Zip(("a.xls",Xls(sheetType:0x40))),"Excel4 macro sheet denied");
        await Reject(Zip(("a.xls",Xls(extra:0x2F))),"encrypted BIFF denied");
        await Reject(Zip(("a.xls",Xls(extra:0x23))),"external name/DDE denied");
        var many=Enumerable.Range(0,130).Select(i=>(i+".pdf",pdf)).ToArray();
        await Reject(Zip(many.Concat(new[]{("nested.zip",Zip(many))}).ToArray()),"global entry budget across layers");
        var corrupt=Zip(("a.pdf",pdf));var central=Find(corrupt,[0x50,0x4b,1,2]);corrupt[14]^=1;corrupt[central+16]^=1;
        await Reject(Zip(("a.zip",corrupt)),"nested CRC failure");
        var form=Office("PAGE \\* MERGEFORMAT");Check((await Validate(Zip(("a.docx",form)))).Count==1,"trusted inert PAGE field accepted");
        await Reject(Zip(("a.docx",Office("DDEAUTO cmd /c calc"))),"DDE Word field denied");
        await Reject(Zip(("a.docx",form)),"ordinary Office policy unchanged",false);
        var officePolicy=typeof(OfficeDocumentPolicy).Assembly.GetType("LandPortal.Infrastructure.TrustedArchiveOfficePolicy")!.GetMethod("Allowed")!;
        bool Formula(string value,string? name=null)=> (bool)officePolicy.Invoke(null,[new System.Xml.Linq.XElement(name==null?"f":"definedName",name==null?null:new System.Xml.Linq.XAttribute("name",name),value),OfficeDocumentPolicy.Xlsx])!;
        Check(Formula("SUM(A1:A3)+ROUND(B2,2)") && Formula("COUNTIF(ONT!L7:L47,bỔ SUNG MỚI)"),"local arithmetic/count formulas; undefined names retained without execution");
        foreach(var f in new[]{"EXEC(\"cmd\")","CALL(\"x\",\"y\",\"z\")","WEBSERVICE(\"https://example.invalid\")","HYPERLINK(\"file:///C:/x\")","_xlfn.WEBSERVICE(\"x\")","cmd|' /c calc'!A1","'[outside.xls]Sheet1'!A1","LAMBDA(x,x)(1)"})
            Check(!Formula(f),"active/external/UDF formula rejected: "+f.Split('(')[0]);
        Check(!Formula("SUM(A1:A3)","Auto_Open"),"custom/auto-run names denied");
        Check(EditorialZipPolicy.Maximum==128L*1024*1024 && EditorialZipPolicy.ExpandedMaximum==512L*1024*1024,"container/expanded limits unchanged");
        Console.WriteLine($"NESTED ZIP {count}/{count}");
    }
    private static int Find(byte[] b,byte[] needle){for(var i=0;i<=b.Length-needle.Length;i++)if(b.AsSpan(i,needle.Length).SequenceEqual(needle))return i;throw new Exception("Missing header");}
    private static byte[] Office(string field)
    {
        using var m=new MemoryStream();using(var zip=new ZipArchive(m,ZipArchiveMode.Create,true))
        {
            void Add(string n,string v){using var w=new StreamWriter(zip.CreateEntry(n).Open());w.Write(v);}
            Add("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/word/document.xml\" ContentType=\""+OfficeDocumentPolicy.Docx+".main+xml\"/></Types>");
            Add("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/document.xml","<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:instrText>"+field+"</w:instrText></w:r></w:p></w:body></w:document>");
        }return m.ToArray();
    }
    private static byte[] Xls(bool macro=false,ushort sheetType=16,ushort? extra=null)
    {
        using var b=new MemoryStream();using var w=new BinaryWriter(b);
        void Record(ushort id,byte[] d){w.Write(id);w.Write((ushort)d.Length);w.Write(d);}
        byte[] Bof(ushort type){var d=new byte[16];BitConverter.GetBytes((ushort)0x600).CopyTo(d,0);BitConverter.GetBytes(type).CopyTo(d,2);return d;}
        Record(0x809,Bof(5));var bound=new byte[9];BitConverter.GetBytes((uint)37).CopyTo(bound,0);bound[6]=1;bound[8]=(byte)'A';Record(0x85,bound);Record(0xA,[]);
        Record(0x809,Bof(sheetType));Record(0x81,[0,0]);if(extra.HasValue)Record(extra.Value,[]);Record(0xA,[]);
        using var m=new MemoryStream();using(var root=RootStorage.Create(m,flags:StorageModeFlags.LeaveOpen)){using(var s=root.CreateStream("Workbook"))s.Write(b.ToArray());if(macro)root.CreateStorage("_VBA_PROJECT_CUR");root.Flush();}return m.ToArray();
    }
    private static async Task Real(string path,string output)
    {
        var before=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        if(before!="8df1b096400344996af1c84841e1c56d5638b1573967436e90eea512151f5eaf" || new FileInfo(path).Length!=124514760)throw new Exception("Source identity mismatch");
        var watch=Stopwatch.StartNew();IReadOnlyList<EditorialZipPolicy.Entry>? entries=null;string? error=null;
        try{entries=await EditorialZipPolicy.ValidateAsync(path,trustedInternal:true);}catch(Exception e){error=e.Message;}
        if (error == null)
        {
            // Independently enumerate both source layers and compare every byte/hash.
            var expected = new Dictionary<string,(long Bytes,string Hash)>();
            int directories=0;
            void Inspect(Stream stream,string prefix)
            {
                using var zip=new ZipArchive(stream,ZipArchiveMode.Read,true);
                foreach(var entry in zip.Entries)
                {
                    if(entry.FullName.EndsWith('/')){directories++;continue;}
                    using var data=new MemoryStream();using(var source=entry.Open())source.CopyTo(data);
                    expected.Add(prefix+entry.FullName,(data.Length,Convert.ToHexStringLower(SHA256.HashData(data.ToArray()))));
                    if(entry.FullName.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)){data.Position=0;Inspect(data,prefix+entry.FullName+"!/");}
                }
            }
            using(var source=File.OpenRead(path))Inspect(source,"");
            if(expected.Count!=166 || directories!=10 || expected.Values.Sum(x=>x.Bytes)!=152677936 || entries!.Count!=expected.Count ||
                entries.Any(x=>!expected.TryGetValue(x.Name,out var original)||original.Bytes!=x.Bytes||original.Hash!=x.Sha256))
                error="Independent source inventory/hash mismatch";
        }
        var after=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var result=new{before,after,bytesUnchanged=before==after,entries,error,milliseconds=watch.ElapsedMilliseconds,sqlWrites=0,scanStatus="NotScanned"};
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(error==null?$"REAL ZIP PASS {entries!.Count} files/containers; {watch.ElapsedMilliseconds}ms; SHA unchanged": "REAL ZIP BLOCKED: "+error);
        if(before!=after || error!=null)Environment.ExitCode=1;
    }
}

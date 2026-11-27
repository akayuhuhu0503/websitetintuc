using System.Security.Cryptography;
using System.Text;
using LandPortal.Infrastructure;

static class SourcePublicationPdfChecks
{
    public static void Run(string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(Path.Combine(Directory.GetCurrentDirectory(),".local")+Path.DirectorySeparatorChar)||Directory.Exists(output))throw new ArgumentException("New local output required");
        Directory.CreateDirectory(output);var passed=0;
        void Check(string name,string extra,string[] objects,bool accept,CancellationToken ct=default)
        {
            var bytes=Pdf(extra,objects);var sha=SHA256.HashData(bytes);var path=Path.Combine(output,(passed+1)+".pdf");File.WriteAllBytes(path,bytes);var ok=true;
            try{ManagedPdfPolicy.Validate(path,ct);}catch(Exception e)when(e is InvalidDataException or OperationCanceledException){ok=false;}
            if(ok!=accept||!sha.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))))throw new Exception(name+": unexpected PDF outcome");
            Console.WriteLine("PASS "+(++passed)+": "+name);
        }
        Check("signature field without byte modification","/AcroForm << /Fields [4 0 R] /SigFlags 3 >>",["<< /FT /Sig /V << /ByteRange [0 10 20 30] /Contents <00> >> >>"],true);
        Check("tagged layout A is passive","/StructTreeRoot 4 0 R",["<< /Type /StructTreeRoot /K 5 0 R >>","<< /Type /StructElem /S /P /P 4 0 R /A << /O /Layout /Placement /Block >> >>"],true);
        Check("click-only HTTPS link","/A << /S /URI /URI (https://example.org/) >>",[],true);
        Check("click-only internal page navigation","/A << /S /GoTo /D [3 0 R /Fit] >>",[],true);
        Check("empty form appearance flag has no fields to execute","/AcroForm << /Fields [] /NeedAppearances true >>",[],true);
        Check("nonempty form appearance regeneration still denied","/AcroForm << /Fields [4 0 R] /NeedAppearances true >>",["<< /FT /Sig >>"],false);
        foreach(var action in new[]{"/OpenAction << /S /GoTo /D [3 0 R /Fit] >>","/AA << /O << /S /JavaScript /JS (bad) >> >>","/A << /S /JavaScript /JS (bad) >>","/A << /S /Launch /F (cmd.exe) >>","/A << /S /URI /URI (file:///private) >>","/A << /S /URI /URI (javascript:alert) >>","/A << /S /URI /URI (https://example.org/) /Next << /S /Launch >> >>","/Names << /EmbeddedFiles << >> >>","/AcroForm << /XFA (bad) >>"})Check("dangerous structure refused: "+action.Split(' ')[0],action,[],false);
        Check("interactive text form denied","/AcroForm << /Fields [4 0 R] >>",["<< /FT /Tx >>"],false);
        Check("malformed signature denied","/AcroForm << /Fields [4 0 R] >>",["<< /FT /Sig /V << /ByteRange [0 10 5 30] /Contents <00> >> >>"],false);
        Check("signature graph cycle denied","/AcroForm << /Fields [4 0 R] >>",["<< /FT /Sig /Kids [4 0 R] >>"],false);
        Check("layout cannot conceal script","/StructTreeRoot 4 0 R",["<< /Type /StructElem /A << /S /JavaScript /JS (bad) >> >>"],false);
        Check("managed token budget unchanged","/Other ["+string.Concat(Enumerable.Repeat("0 ",100_001))+"]",[],false);
        Check("depth budget unchanged","/Other "+string.Concat(Enumerable.Repeat("<< /X ",70))+"0"+string.Concat(Enumerable.Repeat(" >>",70)),[],false);
        Check("cancel honoured","",[],false,new CancellationToken(true));
        File.WriteAllText(Path.Combine(output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{passed,sqlTouched=false}));
    }
    static byte[] Pdf(string extra,string[] additional)
    {
        var objects=new List<string>{"<< /Type /Catalog /Pages 2 0 R "+extra+" >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>"};objects.AddRange(additional);
        var text=new StringBuilder("%PDF-1.7\n");var offsets=new List<int>();
        for(var i=0;i<objects.Count;i++){offsets.Add(text.Length);text.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n");}
        var xref=text.Length;text.Append($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n");foreach(var offset in offsets)text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");return Encoding.ASCII.GetBytes(text.ToString());
    }
}

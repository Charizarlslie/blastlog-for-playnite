using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
namespace Blastlog.Sync.Core {
// Own the handler and disable redirects when creating HttpClient to avoid forwarding credentials.
public sealed class BlastlogClient {
readonly HttpClient http; readonly Uri root; readonly string token;
static readonly JsonSerializerSettings Json = new JsonSerializerSettings { ContractResolver=new CamelCasePropertyNamesContractResolver(), NullValueHandling=NullValueHandling.Ignore };
public BlastlogClient(HttpClient http, Uri server, string token) { if(server == null || !server.IsAbsoluteUri || (server.Scheme != "https" && !(server.Scheme == "http" && server.IsLoopback)) || server.UserInfo.Length>0 || server.Query.Length>0 || server.Fragment.Length>0) throw new ArgumentException("Use HTTPS, or loopback HTTP for tests",nameof(server)); this.http=http ?? throw new ArgumentNullException(nameof(http)); root=new Uri(server.AbsoluteUri.TrimEnd('/')+"/api/sync/v1/"); this.token=token; }
static string Escape(string value) => Uri.EscapeDataString(value ?? throw new ArgumentNullException(nameof(value)));
async Task<Response<T>> Send<T>(HttpMethod method,string path,object body,string etag,CancellationToken ct) {
using(var request=new HttpRequestMessage(method,new Uri(root,path))) { if(!string.IsNullOrEmpty(token)) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token); if(etag!=null) request.Headers.IfNoneMatch.ParseAdd(etag); if(body!=null) request.Content=new StringContent(JsonConvert.SerializeObject(body,Json),Encoding.UTF8,"application/json");
using(var response=await http.SendAsync(request,ct).ConfigureAwait(false)) { var tag=response.Headers.ETag?.ToString(); if(response.StatusCode==HttpStatusCode.NotModified) return new Response<T>{NotModified=true,ETag=tag}; var text=await response.Content.ReadAsStringAsync().ConfigureAwait(false); if(!response.IsSuccessStatusCode) { JObject error; try {error=JObject.Parse(text);} catch(JsonException) {error=new JObject();} var issues=error["issues"] as JArray; var details=issues==null ? null : string.Join("; ",System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(issues,i=>string.Join(".",(i["path"] as JArray ?? new JArray()).Select(p=>(string)p))+" "+(string)i["code"]),5)); throw new BlastlogException((int)response.StatusCode,(string)error["error"] ?? "http_error",(int?)error["retryAfterSeconds"],details); } return new Response<T>{Value=JsonConvert.DeserializeObject<T>(text,Json),ETag=tag}; }
}}
public Task<Response<LibraryPage>> LibraryAsync(string since=null,int limit=500,CancellationToken ct=default) => Send<LibraryPage>(HttpMethod.Get,"library?limit="+limit+(since==null?"":"&since="+Escape(since)),null,null,ct);
/// <summary>Any mirror read by its path under /sync/v1 (e.g. "next-up?mode=quick"), with If-None-Match.</summary>
public Task<Response<JObject>> GetAsync(string relative,string etag=null,CancellationToken ct=default) { if(string.IsNullOrEmpty(relative) || relative.StartsWith("/") || relative.Contains("..") || relative.Contains("://")) throw new ArgumentException("A path under /sync/v1",nameof(relative)); return Send<JObject>(HttpMethod.Get,relative,null,etag,ct); }
public Task<Response<JObject>> MeAsync(string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"me",null,etag,ct);
public Task<Response<JObject>> NextUpAsync(string mode="recommended",string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"next-up?mode="+Escape(mode),null,etag,ct);
public Task<Response<JObject>> BacklogAsync(string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"backlog",null,etag,ct);
public Task<Response<JObject>> DashboardAsync(string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"dashboard",null,etag,ct);
public Task<Response<JObject>> PlayNextAsync(string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"play-next",null,etag,ct);
public Task<Response<JObject>> MadeForYouAsync(string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"made-for-you",null,etag,ct);
public Task<Response<JObject>> NowPlayingAsync(string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"now-playing",null,etag,ct);
public Task<Response<JObject>> CollectionsAsync(string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"collections",null,etag,ct);
public Task<Response<JObject>> TasteProfileAsync(string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"taste-profile",null,etag,ct);
public Task<Response<JObject>> RunsAsync(string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"runs",null,etag,ct);
public Task<Response<JObject>> EntryAsync(string id,string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"entries/"+Escape(id),null,etag,ct);
public Task<Response<JObject>> HistoryAsync(string id,string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"entries/"+Escape(id) + "/history",null,etag,ct);
public Task<Response<JObject>> TraitAsync(string id,string etag=null,CancellationToken ct=default) => Send<JObject>(HttpMethod.Get,"traits/"+Escape(id),null,etag,ct);
public Task<Response<JObject>> FingerprintsAsync(string[] entryIds,string etag=null,CancellationToken ct=default) { if(entryIds==null || entryIds.Length>100) throw new ArgumentException("At most 100 entry IDs"); return Send<JObject>(HttpMethod.Get,"fingerprints?entryIds="+string.Join(",",Array.ConvertAll(entryIds,Escape)),null,etag,ct); }
public Task<Response<MatchResponse>> MatchAsync(System.Collections.Generic.IList<MatchGame> games,CancellationToken ct=default) { if(games==null || games.Count==0 || games.Count>200) throw new ArgumentException("1 to 200 games"); return Send<MatchResponse>(HttpMethod.Post,"match",new {games},null,ct); }
public Task<Response<PushResponse>> PushAsync(PushRequest push,CancellationToken ct=default) => Send<PushResponse>(HttpMethod.Post,"push",push,null,ct);
public Task<Response<UndoResponse>> UndoAsync(string id,CancellationToken ct=default) => Send<UndoResponse>(HttpMethod.Post,"runs/"+Escape(id)+"/undo",new {},null,ct);
public Task<Response<JObject>> UnlinkAsync(string id,CancellationToken ct=default) => Send<JObject>(HttpMethod.Post,"links/"+Escape(id)+"/unlink",new {},null,ct);
}}

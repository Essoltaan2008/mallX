import { useEffect, useState } from 'react';
import {
  BarChart, Bar, LineChart, Line, XAxis, YAxis,
  CartesianGrid, Tooltip, ResponsiveContainer, PieChart, Pie, Cell, Legend
} from 'recharts';

// ─── API helper ───────────────────────────────────────────────────────────
const API = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000/api';
async function apiFetch(path: string, token: string) {
  const r = await fetch(`${API}${path}`, {
    headers: { Authorization: `Bearer ${token}` }
  });
  if (!r.ok) throw new Error(await r.text());
  return r.json();
}

// ─── Types ────────────────────────────────────────────────────────────────
interface Dashboard   { totalRevenue:number; totalCommission:number; totalOrders:number; activeStores:number; totalCustomers:number; topStores:TopStore[] }
interface TopStore    { storeId:string; storeName:string; revenue:number; orders:number; commission:number }
interface RevenueBreakdown { totalRevenue:number; totalCommission:number; totalOrders:number; avgOrderValue:number; avgCommissionRate:number; byStore:StoreReport[]; daily:DailyRevenue[] }
interface StoreReport { storeId:string; storeName:string; totalOrders:number; grossRevenue:number; commissionRate:number; commissionAmt:number; netPayable:number }
interface DailyRevenue { date:string; revenue:number; commission:number; orders:number }

const COLORS = ['#3b82f6','#10b981','#f59e0b','#8b5cf6','#ef4444'];

const fmt = (n:number) => n?.toLocaleString('ar-EG', {minimumFractionDigits:0}) ?? '0';
const fmtPct = (n:number) => `${(n*100).toFixed(1)}%`;

// ─── Stat Card ────────────────────────────────────────────────────────────
function StatCard({ label, value, sub, icon, color='#3b82f6' }:
  { label:string; value:string; sub?:string; icon:string; color?:string }) {
  return (
    <div className="card">
      <div className="flex items-start justify-between">
        <div>
          <p className="text-slate-400 text-sm mb-1">{label}</p>
          <p className="text-2xl font-bold text-slate-100">{value}</p>
          {sub && <p className="text-xs mt-1" style={{color}}>{sub}</p>}
        </div>
        <div className="text-3xl">{icon}</div>
      </div>
    </div>
  );
}

// ─── Main Component ───────────────────────────────────────────────────────
export default function MallAdminDashboard() {
  const [dash, setDash]       = useState<Dashboard|null>(null);
  const [report, setReport]   = useState<RevenueBreakdown|null>(null);
  const [loading, setLoading] = useState(true);
  const [tab, setTab]         = useState<'overview'|'commission'|'stores'>('overview');
  const [dateFrom, setDateFrom] = useState(() => {
    const d = new Date(); d.setDate(d.getDate()-30);
    return d.toISOString().slice(0,10);
  });
  const [dateTo, setDateTo] = useState(() => new Date().toISOString().slice(0,10));

  const token = typeof window !== 'undefined'
    ? localStorage.getItem('mx_token') ?? '' : '';

  const loadData = async () => {
    setLoading(true);
    try {
      const [d, r] = await Promise.all([
        apiFetch('/mall/admin/dashboard', token),
        apiFetch(`/mall/admin/commission/report?from=${dateFrom}&to=${dateTo}`, token),
      ]);
      setDash(d.data);
      setReport(r.data);
    } catch(e) { console.error(e); }
    setLoading(false);
  };

  useEffect(() => { loadData(); }, [dateFrom, dateTo]);

  if (loading) return (
    <div className="min-h-screen flex items-center justify-center">
      <div className="text-blue-400 text-lg animate-pulse">⏳ جاري تحميل البيانات...</div>
    </div>
  );

  return (
    <div className="min-h-screen p-6 max-w-7xl mx-auto" dir="rtl">

      {/* Header */}
      <div className="flex items-center justify-between mb-8 flex-wrap gap-4">
        <div>
          <h1 className="text-2xl font-bold text-slate-100">🏬 لوحة تحكم المول</h1>
          <p className="text-slate-400 text-sm mt-1">MallX Admin — نظرة شاملة</p>
        </div>
        {/* Date range */}
        <div className="flex gap-2 items-center flex-wrap">
          <input type="date" value={dateFrom} onChange={e => setDateFrom(e.target.value)}
            className="input-dark text-sm" />
          <span className="text-slate-400">→</span>
          <input type="date" value={dateTo} onChange={e => setDateTo(e.target.value)}
            className="input-dark text-sm" />
          <button onClick={loadData} className="btn-primary text-sm px-4 py-2">تحديث</button>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-4 mb-8">
        <StatCard label="إجمالي الإيرادات" value={`${fmt(report?.totalRevenue??0)} ج.م`} icon="💰" color="#10b981" />
        <StatCard label="عمولة المول" value={`${fmt(report?.totalCommission??0)} ج.م`} icon="📊" color="#3b82f6"
          sub={`${report?.avgCommissionRate?.toFixed(1) ?? 0}% متوسط`} />
        <StatCard label="إجمالي الطلبات" value={fmt(report?.totalOrders??0)} icon="🛒" />
        <StatCard label="المتاجر النشطة" value={fmt(dash?.activeStores??0)} icon="🏪" />
        <StatCard label="إجمالي العملاء" value={fmt(dash?.totalCustomers??0)} icon="👥" />
      </div>

      {/* Tabs */}
      <div className="flex gap-2 mb-6 border-b border-slate-700 pb-0">
        {([['overview','نظرة عامة'],['commission','العمولات'],['stores','المتاجر']] as const).map(([id,label]) => (
          <button key={id} onClick={() => setTab(id)}
            className={`px-4 py-2 text-sm font-semibold border-b-2 transition-colors ${
              tab===id ? 'border-blue-500 text-blue-400' : 'border-transparent text-slate-400 hover:text-slate-300'
            }`}>
            {label}
          </button>
        ))}
      </div>

      {/* ─── OVERVIEW TAB ─── */}
      {tab === 'overview' && (
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Revenue Chart */}
          <div className="card lg:col-span-2">
            <h2 className="font-semibold mb-4 text-slate-200">📈 الإيرادات اليومية</h2>
            <ResponsiveContainer width="100%" height={240}>
              <BarChart data={report?.daily ?? []}>
                <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" />
                <XAxis dataKey="date" tick={{fill:'#64748b', fontSize:10}}
                  tickFormatter={d => new Date(d).toLocaleDateString('ar-EG',{day:'numeric',month:'short'})} />
                <YAxis tick={{fill:'#64748b', fontSize:10}} />
                <Tooltip contentStyle={{background:'#1e293b',border:'1px solid #334155',color:'#f1f5f9', fontSize:12}}
                  formatter={(v:number) => [`${fmt(v)} ج.م`]} />
                <Bar dataKey="revenue" name="الإيرادات" fill="#3b82f6" radius={[4,4,0,0]} />
                <Bar dataKey="commission" name="العمولة" fill="#10b981" radius={[4,4,0,0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>

          {/* Top Stores Pie */}
          <div className="card">
            <h2 className="font-semibold mb-4 text-slate-200">🏆 أفضل المتاجر</h2>
            <ResponsiveContainer width="100%" height={200}>
              <PieChart>
                <Pie data={dash?.topStores??[]} dataKey="revenue" nameKey="storeName"
                  cx="50%" cy="50%" outerRadius={70} label={({storeName}) => storeName?.slice(0,8)}>
                  {(dash?.topStores??[]).map((_, i) => (
                    <Cell key={i} fill={COLORS[i % COLORS.length]} />
                  ))}
                </Pie>
                <Tooltip formatter={(v:number) => [`${fmt(v)} ج.م`]} />
              </PieChart>
            </ResponsiveContainer>
            <div className="mt-3 space-y-1">
              {(dash?.topStores??[]).slice(0,4).map((s, i) => (
                <div key={s.storeId} className="flex justify-between text-xs">
                  <span className="flex items-center gap-2">
                    <span className="w-2 h-2 rounded-full inline-block"
                      style={{background: COLORS[i%COLORS.length]}} />
                    <span className="text-slate-300">{s.storeName}</span>
                  </span>
                  <span className="text-slate-400">{fmt(s.orders)} طلب</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* ─── COMMISSION TAB ─── */}
      {tab === 'commission' && (
        <div className="space-y-6">
          {/* Summary banner */}
          <div className="grid grid-cols-3 gap-4">
            {[
              { label:'إجمالي الإيرادات', val:`${fmt(report?.totalRevenue??0)} ج.م`, color:'#3b82f6' },
              { label:'عمولة المول',       val:`${fmt(report?.totalCommission??0)} ج.م`, color:'#10b981' },
              { label:'المدفوع للمتاجر',  val:`${fmt((report?.totalRevenue??0)-(report?.totalCommission??0))} ج.م`, color:'#f59e0b' },
            ].map(({label,val,color}) => (
              <div key={label} className="card text-center">
                <p className="text-slate-400 text-sm">{label}</p>
                <p className="text-xl font-bold mt-1" style={{color}}>{val}</p>
              </div>
            ))}
          </div>

          {/* Commission table */}
          <div className="card overflow-x-auto">
            <h2 className="font-semibold mb-4 text-slate-200">تفاصيل العمولة حسب المتجر</h2>
            <table className="w-full text-sm">
              <thead>
                <tr className="text-slate-400 text-xs uppercase border-b border-slate-700">
                  <th className="text-right pb-2">المتجر</th>
                  <th className="text-left pb-2">الطلبات</th>
                  <th className="text-left pb-2">الإيرادات</th>
                  <th className="text-left pb-2">نسبة العمولة</th>
                  <th className="text-left pb-2">العمولة</th>
                  <th className="text-left pb-2">المستحق</th>
                </tr>
              </thead>
              <tbody>
                {(report?.byStore??[]).map(s => (
                  <tr key={s.storeId} className="border-b border-slate-800 hover:bg-slate-800/50 transition-colors">
                    <td className="py-3 font-medium text-slate-200">{s.storeName}</td>
                    <td className="py-3 text-slate-400">{s.totalOrders}</td>
                    <td className="py-3 text-slate-300">{fmt(s.grossRevenue)} ج.م</td>
                    <td className="py-3">
                      <span className="badge-blue">{fmtPct(s.commissionRate)}</span>
                    </td>
                    <td className="py-3 text-green-400 font-semibold">{fmt(s.commissionAmt)} ج.م</td>
                    <td className="py-3 text-blue-400 font-semibold">{fmt(s.netPayable)} ج.م</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="font-bold text-slate-100 border-t border-slate-600">
                  <td className="pt-3">الإجمالي</td>
                  <td className="pt-3">{report?.totalOrders}</td>
                  <td className="pt-3">{fmt(report?.totalRevenue??0)} ج.م</td>
                  <td className="pt-3">—</td>
                  <td className="pt-3 text-green-400">{fmt(report?.totalCommission??0)} ج.م</td>
                  <td className="pt-3 text-blue-400">{fmt((report?.totalRevenue??0)-(report?.totalCommission??0))} ج.م</td>
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
      )}

      {/* ─── STORES TAB ─── */}
      {tab === 'stores' && (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {(report?.byStore??[]).map((s, i) => (
            <div key={s.storeId} className="card space-y-3">
              <div className="flex items-center justify-between">
                <span className="font-bold text-slate-200">{s.storeName}</span>
                <span className="badge-blue">{fmtPct(s.commissionRate)}</span>
              </div>
              <div className="grid grid-cols-2 gap-2 text-sm">
                <div><p className="text-slate-400 text-xs">الطلبات</p><p className="font-bold text-slate-200">{s.totalOrders}</p></div>
                <div><p className="text-slate-400 text-xs">الإيرادات</p><p className="font-bold text-slate-200">{fmt(s.grossRevenue)}</p></div>
                <div><p className="text-slate-400 text-xs">العمولة</p><p className="font-bold text-green-400">{fmt(s.commissionAmt)}</p></div>
                <div><p className="text-slate-400 text-xs">المستحق</p><p className="font-bold text-blue-400">{fmt(s.netPayable)}</p></div>
              </div>
              {/* Revenue bar */}
              <div className="w-full h-1.5 bg-slate-700 rounded-full overflow-hidden">
                <div className="h-full rounded-full" style={{
                  width: `${Math.min(100, (s.grossRevenue/(report?.totalRevenue||1))*100)}%`,
                  background: COLORS[i % COLORS.length]
                }} />
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Global CSS helpers */}
      <style jsx global>{`
        .card { background:#0f172a; border:1px solid #1e293b; border-radius:14px; padding:20px; }
        .input-dark { background:#0f172a; border:1px solid #334155; color:#e2e8f0;
          border-radius:8px; padding:6px 12px; outline:none; }
        .btn-primary { background:#3b82f6; color:white; border:none;
          border-radius:8px; cursor:pointer; font-weight:600; }
        .badge-blue { background:rgba(59,130,246,0.15); color:#60a5fa;
          border-radius:6px; padding:2px 8px; font-size:11px; font-weight:600; }
      `}</style>
    </div>
  );
}

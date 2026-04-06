import { useEffect, useState } from 'react';
import {
  AreaChart, Area, BarChart, Bar, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend
} from 'recharts';

const API = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000/api';
const auth = () => ({ Authorization: `Bearer ${localStorage.getItem('mx_token')}` });

const PERIODS = [
  { value: 'today',   label: 'اليوم' },
  { value: 'week',    label: '7 أيام' },
  { value: 'month',   label: 'الشهر' },
  { value: 'quarter', label: 'ربع سنة' },
  { value: 'year',    label: 'السنة' },
];
const COLORS = ['#3b82f6','#10b981','#f59e0b','#8b5cf6','#ef4444','#06b6d4'];
const fmt    = (n: number) => n?.toLocaleString('ar-EG', {minimumFractionDigits:0}) ?? '0';
const pct    = (n: number) => `${n > 0 ? '+' : ''}${n?.toFixed(1) ?? 0}%`;

interface Analytics {
  period: string;
  revenue: { total:number; totalCommission:number; avgOrderValue:number; growthPct:number; growthAmount:number };
  orders:  { total:number; completed:number; cancelled:number; successRate:number; growthPct:number; byFulfillmentType:Record<string,number>; byPaymentMethod:Record<string,number> };
  customers: { totalActive:number; newThisPeriod:number; retentionRate:number; byTier:Record<string,number> };
  revenueChart: {date:string; value:number; count:number}[];
  ordersChart:  {date:string; value:number; count:number}[];
  topStores: {storeId:string; storeName:string; storeType:string; revenue:number; orders:number; commission:number; rating:number}[];
  hourlySales: {hour:number; revenue:number; orders:number}[];
  loyalty: { pointsIssued:number; pointsRedeemed:number; activeAccounts:number; byTier:Record<string,number> };
}

function KpiCard({ label, value, sub, color='#3b82f6', growth, icon }:
  { label:string; value:string; sub?:string; color?:string; growth?:number; icon:string }) {
  return (
    <div className="card">
      <div className="flex items-start justify-between">
        <div className="flex-1">
          <p className="text-slate-400 text-xs mb-1">{label}</p>
          <p className="text-2xl font-bold text-slate-100">{value}</p>
          {sub && <p className="text-xs text-slate-500 mt-1">{sub}</p>}
          {growth !== undefined && (
            <span className={`text-xs font-semibold ${growth >= 0 ? 'text-green-400' : 'text-red-400'}`}>
              {pct(growth)} مقارنة بالفترة السابقة
            </span>
          )}
        </div>
        <span className="text-3xl">{icon}</span>
      </div>
    </div>
  );
}

export default function AnalyticsDashboard() {
  const [data, setData]       = useState<Analytics | null>(null);
  const [period, setPeriod]   = useState('month');
  const [loading, setLoading] = useState(true);
  const [tab, setTab]         = useState<'overview'|'stores'|'customers'|'loyalty'>('overview');

  useEffect(() => {
    setLoading(true);
    fetch(`${API}/mall/admin/analytics?period=${period}`, { headers: auth() })
      .then(r => r.json())
      .then(d => { setData(d.data); setLoading(false); })
      .catch(() => setLoading(false));
  }, [period]);

  const hourlyData = data?.hourlySales?.map(h => ({
    name: `${h.hour}:00`, revenue: h.revenue, orders: h.orders
  })) ?? [];

  const pieDataFulfillment = Object.entries(data?.orders?.byFulfillmentType ?? {}).map(([k,v]) => ({
    name: k === 'Delivery' ? 'توصيل' : k === 'Pickup' ? 'استلام' : k, value: v
  }));

  const pieDataTier = Object.entries(data?.customers?.byTier ?? {}).map(([k,v]) => ({
    name: k, value: v
  }));

  if (loading) return (
    <div className="min-h-screen flex items-center justify-center bg-slate-950">
      <div className="text-blue-400 animate-pulse text-lg">⏳ جاري تحميل التقارير...</div>
    </div>
  );

  return (
    <div className="min-h-screen p-6 max-w-7xl mx-auto" dir="rtl">

      {/* Header */}
      <div className="flex items-center justify-between mb-8 flex-wrap gap-4">
        <div>
          <h1 className="text-2xl font-bold text-slate-100">📊 التقارير والتحليلات</h1>
          <p className="text-slate-400 text-sm mt-1">{data?.period ?? '—'}</p>
        </div>
        {/* Period Selector */}
        <div className="flex gap-2 bg-slate-900 p-1 rounded-xl border border-slate-700">
          {PERIODS.map(p => (
            <button key={p.value} onClick={() => setPeriod(p.value)}
              className={`px-4 py-2 rounded-lg text-sm font-semibold transition-all ${
                period === p.value
                  ? 'bg-blue-600 text-white'
                  : 'text-slate-400 hover:text-slate-200'
              }`}>
              {p.label}
            </button>
          ))}
        </div>
      </div>

      {/* KPI Strip */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        <KpiCard label="إجمالي الإيرادات" value={`${fmt(data?.revenue.total ?? 0)} ج.م`}
          icon="💰" growth={data?.revenue.growthPct}
          sub={`متوسط الطلب: ${fmt(data?.revenue.avgOrderValue ?? 0)} ج.م`} />
        <KpiCard label="عمولة المول" value={`${fmt(data?.revenue.totalCommission ?? 0)} ج.م`}
          icon="📊" color="#10b981" />
        <KpiCard label="إجمالي الطلبات" value={fmt(data?.orders.total ?? 0)}
          icon="🛒" growth={data?.orders.growthPct}
          sub={`نسبة الإتمام: ${data?.orders.successRate ?? 0}%`} />
        <KpiCard label="العملاء النشطون" value={fmt(data?.customers.totalActive ?? 0)}
          icon="👥" color="#8b5cf6"
          sub={`جدد هذه الفترة: ${data?.customers.newThisPeriod ?? 0}`} />
      </div>

      {/* Tabs */}
      <div className="flex gap-2 mb-6 border-b border-slate-800">
        {([['overview','نظرة عامة'],['stores','المتاجر'],['customers','العملاء'],['loyalty','الولاء']] as const)
          .map(([id, label]) => (
          <button key={id} onClick={() => setTab(id)}
            className={`px-4 py-2 text-sm font-semibold border-b-2 transition-colors ${
              tab === id ? 'border-blue-500 text-blue-400' : 'border-transparent text-slate-400'
            }`}>
            {label}
          </button>
        ))}
      </div>

      {/* ── OVERVIEW ── */}
      {tab === 'overview' && (
        <div className="space-y-6">
          {/* Revenue Area Chart */}
          <div className="card">
            <h3 className="font-semibold text-slate-200 mb-4">📈 تطور الإيرادات</h3>
            <ResponsiveContainer width="100%" height={220}>
              <AreaChart data={data?.revenueChart ?? []}>
                <defs>
                  <linearGradient id="rev" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#3b82f6" stopOpacity={0.2}/>
                    <stop offset="95%" stopColor="#3b82f6" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#1e293b"/>
                <XAxis dataKey="date" tick={{fill:'#64748b', fontSize:10}}
                  tickFormatter={d => new Date(d).toLocaleDateString('ar-EG',{day:'numeric',month:'short'})}/>
                <YAxis tick={{fill:'#64748b', fontSize:10}}/>
                <Tooltip contentStyle={{background:'#1e293b',border:'1px solid #334155',color:'#f1f5f9', fontSize:12}}
                  formatter={(v:number) => [`${fmt(v)} ج.م`,'الإيرادات']}/>
                <Area type="monotone" dataKey="value" stroke="#3b82f6" fill="url(#rev)" strokeWidth={2}/>
              </AreaChart>
            </ResponsiveContainer>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {/* Hourly heatmap */}
            <div className="card">
              <h3 className="font-semibold text-slate-200 mb-4">🕐 توزيع المبيعات بالساعة</h3>
              <ResponsiveContainer width="100%" height={180}>
                <BarChart data={hourlyData}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#1e293b"/>
                  <XAxis dataKey="name" tick={{fill:'#64748b', fontSize:9}}/>
                  <YAxis tick={{fill:'#64748b', fontSize:10}}/>
                  <Tooltip contentStyle={{background:'#1e293b',border:'1px solid #334155', fontSize:11}}
                    formatter={(v:number) => [`${fmt(v)} ج.م`]}/>
                  <Bar dataKey="revenue" fill="#3b82f6" radius={[3,3,0,0]}/>
                </BarChart>
              </ResponsiveContainer>
            </div>

            {/* Fulfillment Pie */}
            <div className="card">
              <h3 className="font-semibold text-slate-200 mb-4">🚚 طريقة الاستلام</h3>
              <div className="flex items-center gap-4">
                <ResponsiveContainer width={140} height={140}>
                  <PieChart>
                    <Pie data={pieDataFulfillment} cx="50%" cy="50%" innerRadius={35} outerRadius={60} dataKey="value">
                      {pieDataFulfillment.map((_, i) => <Cell key={i} fill={COLORS[i % COLORS.length]}/>)}
                    </Pie>
                    <Tooltip/>
                  </PieChart>
                </ResponsiveContainer>
                <div className="space-y-2">
                  {pieDataFulfillment.map((entry, i) => (
                    <div key={entry.name} className="flex items-center gap-2 text-sm">
                      <span className="w-3 h-3 rounded-full" style={{background: COLORS[i % COLORS.length]}}/>
                      <span className="text-slate-300">{entry.name}</span>
                      <span className="text-slate-500">{entry.value}</span>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ── STORES ── */}
      {tab === 'stores' && (
        <div className="card overflow-x-auto">
          <h3 className="font-semibold text-slate-200 mb-4">🏪 ترتيب المتاجر بالأداء</h3>
          <table className="w-full text-sm">
            <thead>
              <tr className="text-slate-500 text-xs uppercase border-b border-slate-800">
                <th className="pb-2 text-right">#</th>
                <th className="pb-2 text-right">المتجر</th>
                <th className="pb-2">الطلبات</th>
                <th className="pb-2">الإيرادات</th>
                <th className="pb-2">العمولة</th>
                <th className="pb-2">التقييم</th>
              </tr>
            </thead>
            <tbody>
              {(data?.topStores ?? []).map((s, i) => (
                <tr key={s.storeId} className="border-b border-slate-800 hover:bg-slate-800/30">
                  <td className="py-3 text-slate-500 font-bold">{i+1}</td>
                  <td className="py-3">
                    <div className="font-semibold text-slate-200">{s.storeName}</div>
                    <div className="text-xs text-slate-500">{s.storeType}</div>
                  </td>
                  <td className="py-3 text-center text-slate-300">{s.orders}</td>
                  <td className="py-3 text-center font-semibold text-blue-400">{fmt(s.revenue)} ج.م</td>
                  <td className="py-3 text-center text-green-400">{fmt(s.commission)} ج.م</td>
                  <td className="py-3 text-center">
                    <span className="text-yellow-400">⭐ {s.rating?.toFixed(1) ?? '—'}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── CUSTOMERS ── */}
      {tab === 'customers' && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <div className="card">
            <h3 className="font-semibold text-slate-200 mb-4">👥 توزيع العملاء بالمستوى</h3>
            <div className="flex items-center gap-6">
              <ResponsiveContainer width={160} height={160}>
                <PieChart>
                  <Pie data={pieDataTier} cx="50%" cy="50%" outerRadius={65} dataKey="value" label={({name}) => name}>
                    {pieDataTier.map((_, i) => <Cell key={i} fill={COLORS[i]}/>)}
                  </Pie>
                  <Tooltip/>
                </PieChart>
              </ResponsiveContainer>
              <div className="space-y-3">
                {pieDataTier.map((entry, i) => (
                  <div key={entry.name} className="flex items-center justify-between gap-8">
                    <div className="flex items-center gap-2">
                      <span className="w-3 h-3 rounded-full" style={{background: COLORS[i]}}/>
                      <span className="text-slate-300 text-sm">{entry.name}</span>
                    </div>
                    <span className="font-bold text-slate-200">{entry.value}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
          <div className="card">
            <h3 className="font-semibold text-slate-200 mb-4">📊 إحصاءات العملاء</h3>
            <div className="space-y-4">
              {[
                ['إجمالي العملاء',    data?.customers.totalActive,   '#3b82f6'],
                ['جدد هذه الفترة',   data?.customers.newThisPeriod,  '#10b981'],
                ['العملاء العائدون',  data?.customers.returning,      '#8b5cf6'],
                ['نسبة الاحتفاظ',    `${data?.customers.retentionRate}%`, '#f59e0b'],
              ].map(([label, val, color]) => (
                <div key={String(label)} className="flex justify-between items-center py-2 border-b border-slate-800">
                  <span className="text-slate-400 text-sm">{label}</span>
                  <span className="font-bold text-sm" style={{color: String(color)}}>{val}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* ── LOYALTY ── */}
      {tab === 'loyalty' && (
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          {[
            { label:'نقاط صدرت',  val: fmt(data?.loyalty.pointsIssued ?? 0),   icon:'⭐', color:'#f59e0b' },
            { label:'نقاط استُبدلت', val: fmt(data?.loyalty.pointsRedeemed ?? 0), icon:'🎁', color:'#10b981' },
            { label:'حسابات نشطة', val: fmt(data?.loyalty.activeAccounts ?? 0), icon:'👤', color:'#3b82f6' },
            { label:'معدل الاستبدال', val: data?.loyalty.pointsIssued
                ? `${((data.loyalty.pointsRedeemed / data.loyalty.pointsIssued)*100).toFixed(1)}%`
                : '0%', icon:'📊', color:'#8b5cf6' },
          ].map(({label, val, icon, color}) => (
            <div key={label} className="card text-center">
              <div className="text-3xl mb-2">{icon}</div>
              <div className="text-2xl font-bold" style={{color}}>{val}</div>
              <div className="text-slate-400 text-xs mt-1">{label}</div>
            </div>
          ))}
        </div>
      )}

      <style jsx global>{`
        .card { background:#0f172a; border:1px solid #1e293b; border-radius:14px; padding:20px; }
      `}</style>
    </div>
  );
}

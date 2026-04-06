import { useEffect, useState } from 'react';

const API = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000/api';
const auth = () => ({ 'Content-Type': 'application/json',
  Authorization: `Bearer ${localStorage.getItem('mx_superadmin_token')}` });

const fmt  = (n: number) => n?.toLocaleString('ar-EG') ?? '0';

interface Overview {
  totalMalls:number; totalStores:number; totalCustomers:number;
  totalRevenue:number; totalCommission:number; activeSubs:number; trialSubs:number;
  malls: MallSummary[];
}
interface MallSummary {
  mallId:string; name:string; stores:number; customers:number;
  revenue:number; commission:number; isActive:boolean;
}

function CreateMallModal({ onClose, onCreated }: { onClose:()=>void; onCreated:()=>void }) {
  const [form, setForm] = useState({ name:'', nameAr:'', slug:'', address:'', phone:'', email:'' });
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState('');
  const f = (k:string) => (e:any) => setForm(p => ({...p, [k]: e.target.value}));

  const save = async () => {
    setSaving(true); setErr('');
    try {
      const res = await fetch(`${API}/superadmin/malls`, {
        method:'POST', headers:auth(), body: JSON.stringify(form) });
      const d = await res.json();
      if (d.success) { onCreated(); onClose(); }
      else setErr(d.error ?? 'خطأ غير معروف');
    } catch { setErr('تعذر الاتصال بالسيرفر'); }
    setSaving(false);
  };

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div className="bg-slate-900 border border-slate-700 rounded-2xl p-6 w-full max-w-md" dir="rtl">
        <div className="flex justify-between items-center mb-6">
          <h2 className="text-lg font-bold text-slate-100">🏬 إنشاء مول جديد</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-200 text-xl">✕</button>
        </div>
        <div className="space-y-3">
          {[
            ['name','اسم المول (EN)','Mall X City Centre'],
            ['nameAr','اسم المول (AR)','مول إكس سيتي سنتر'],
            ['slug','الـ Slug (مفرد، بدون مسافات)','mallx-city'],
            ['address','العنوان','القاهرة — مصر'],
            ['phone','الهاتف','+20 2 1234 5678'],
            ['email','البريد الإلكتروني','info@mallx.com'],
          ].map(([k, label, ph]) => (
            <div key={k}>
              <label className="block text-xs text-slate-400 mb-1">{label}</label>
              <input value={(form as any)[k]} onChange={f(k)} placeholder={ph}
                className="w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-2 text-slate-200 text-sm outline-none focus:border-blue-500" />
            </div>
          ))}
        </div>
        {err && <p className="text-red-400 text-sm mt-3">{err}</p>}
        <div className="flex gap-3 mt-6">
          <button onClick={onClose}
            className="flex-1 py-2 border border-slate-700 rounded-lg text-slate-400 text-sm">
            إلغاء
          </button>
          <button onClick={save} disabled={saving || !form.name || !form.slug}
            className="flex-1 py-2 bg-blue-600 hover:bg-blue-700 rounded-lg text-white font-bold text-sm disabled:opacity-40">
            {saving ? '⏳...' : 'إنشاء المول'}
          </button>
        </div>
      </div>
    </div>
  );
}

export default function SuperAdminDashboard() {
  const [data, setData]       = useState<Overview|null>(null);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);

  const load = () => {
    setLoading(true);
    fetch(`${API}/superadmin/overview`, { headers: auth() })
      .then(r => r.json())
      .then(d => { setData(d.data); setLoading(false); })
      .catch(() => setLoading(false));
  };

  useEffect(load, []);

  const suspend = async (mallId: string, storeId: string) => {
    if (!confirm('هل تريد تعليق هذا المحل؟')) return;
    await fetch(`${API}/superadmin/stores/${storeId}/suspend`, {
      method:'POST', headers:auth(), body:JSON.stringify({reason:'تعليق إداري'})});
    load();
  };

  if (loading) return (
    <div className="min-h-screen flex items-center justify-center bg-slate-950">
      <div className="text-blue-400 animate-pulse">⏳ جاري تحميل بيانات المنصة...</div>
    </div>
  );

  return (
    <div className="min-h-screen p-6 max-w-7xl mx-auto" dir="rtl">
      {showModal && <CreateMallModal onClose={() => setShowModal(false)} onCreated={load}/>}

      {/* Header */}
      <div className="flex items-center justify-between mb-8 flex-wrap gap-4">
        <div>
          <h1 className="text-2xl font-bold text-slate-100">🌐 SuperAdmin — إدارة المنصة</h1>
          <p className="text-slate-400 text-sm mt-1">نظرة شاملة على جميع الموالات والمتاجر</p>
        </div>
        <button onClick={() => setShowModal(true)}
          className="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded-lg font-bold text-sm flex items-center gap-2">
          🏬 إنشاء مول جديد
        </button>
      </div>

      {/* Platform KPIs */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        {[
          { label:'الموالات',       val:fmt(data?.totalMalls ?? 0),       icon:'🏬', color:'#3b82f6' },
          { label:'المتاجر',        val:fmt(data?.totalStores ?? 0),      icon:'🏪', color:'#10b981' },
          { label:'العملاء',        val:fmt(data?.totalCustomers ?? 0),   icon:'👥', color:'#8b5cf6' },
          { label:'إيرادات الشهر',  val:`${fmt(data?.totalRevenue ?? 0)} ج.م`, icon:'💰', color:'#f59e0b' },
          { label:'عمولة المنصة',   val:`${fmt(data?.totalCommission ?? 0)} ج.م`, icon:'📊', color:'#ef4444' },
          { label:'اشتراكات نشطة', val:fmt(data?.activeSubs ?? 0),       icon:'✅', color:'#10b981' },
          { label:'فترة تجريبية',   val:fmt(data?.trialSubs ?? 0),        icon:'⏳', color:'#f59e0b' },
          { label:'معدل الاشتراك',  val:data?.totalMalls
              ? `${((data.activeSubs / data.totalMalls)*100).toFixed(0)}%` : '0%',
            icon:'📈', color:'#3b82f6' },
        ].map(({ label, val, icon, color }) => (
          <div key={label} className="card">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-slate-400 text-xs">{label}</p>
                <p className="text-xl font-bold mt-1" style={{color}}>{val}</p>
              </div>
              <span className="text-2xl">{icon}</span>
            </div>
          </div>
        ))}
      </div>

      {/* Malls Table */}
      <div className="card overflow-x-auto">
        <div className="flex items-center justify-between mb-4">
          <h2 className="font-bold text-slate-200">🏬 جميع الموالات</h2>
          <span className="text-xs text-slate-400">{data?.malls.length ?? 0} مول</span>
        </div>
        <table className="w-full text-sm">
          <thead>
            <tr className="text-slate-500 text-xs uppercase border-b border-slate-800">
              <th className="pb-2 text-right">المول</th>
              <th className="pb-2">المتاجر</th>
              <th className="pb-2">العملاء</th>
              <th className="pb-2">الإيرادات</th>
              <th className="pb-2">العمولة</th>
              <th className="pb-2">الحالة</th>
              <th className="pb-2">إجراء</th>
            </tr>
          </thead>
          <tbody>
            {(data?.malls ?? []).map(mall => (
              <tr key={mall.mallId}
                className="border-b border-slate-800 hover:bg-slate-800/30 transition-colors">
                <td className="py-3">
                  <span className="font-semibold text-slate-200">{mall.name}</span>
                </td>
                <td className="py-3 text-center text-slate-300">{mall.stores}</td>
                <td className="py-3 text-center text-slate-300">{fmt(mall.customers)}</td>
                <td className="py-3 text-center text-blue-400 font-semibold">{fmt(mall.revenue)} ج.م</td>
                <td className="py-3 text-center text-green-400">{fmt(mall.commission)} ج.م</td>
                <td className="py-3 text-center">
                  <span className={`badge ${mall.isActive ? 'badge-green' : 'badge-red'}`}>
                    {mall.isActive ? 'نشط' : 'موقوف'}
                  </span>
                </td>
                <td className="py-3 text-center">
                  <button
                    onClick={() => window.location.href = `/superadmin/malls/${mall.mallId}`}
                    className="text-xs text-blue-400 hover:text-blue-300 ml-3">
                    تفاصيل
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Platform Settings Quick Access */}
      <div className="card mt-6">
        <h2 className="font-bold text-slate-200 mb-4">⚙️ إعدادات المنصة</h2>
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
          {[
            ['العمولة الافتراضية', '5%',    'platform.default_commission'],
            ['أيام التجربة',        '14 يوم', 'platform.trial_days'],
            ['إشعارات واتساب',     'متوقف', 'whatsapp.enabled'],
            ['برنامج الإحالة',     'مفعل',  'referral.enabled'],
          ].map(([label, val, key]) => (
            <div key={key} className="bg-slate-800 rounded-xl p-3 border border-slate-700">
              <p className="text-slate-400 text-xs">{label}</p>
              <p className="text-slate-200 font-semibold mt-1">{val}</p>
            </div>
          ))}
        </div>
      </div>

      <style jsx global>{`
        .card { background:#0f172a; border:1px solid #1e293b; border-radius:14px; padding:20px; }
        .badge { padding:2px 10px; border-radius:6px; font-size:11px; font-weight:700; }
        .badge-green { background:rgba(16,185,129,.15); color:#10b981; }
        .badge-red   { background:rgba(239,68,68,.15);  color:#ef4444; }
      `}</style>
    </div>
  );
}

import { useState, useEffect } from 'react';

const API = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000/api';
const auth = () => ({ 'Content-Type': 'application/json',
  Authorization: `Bearer ${localStorage.getItem('mx_token')}` });

type Tab = 'coupons' | 'flash' | 'push';

interface FlashSale { id:string; title:string; flashPrice:number; discountPct:number; remaining:number; isLive:boolean; endsAt:string }
interface Coupon { id:string; code:string; name:string; discountType:string; discountValue:number; usedCount:number; status:string; validTo:string }

const fmt = (n:number) => n?.toLocaleString('ar-EG') ?? '0';

// ─── Input helpers ────────────────────────────────────────────────────────
const Input = ({ label, ...props }: any) => (
  <div>
    <label className="block text-xs text-slate-400 mb-1">{label}</label>
    <input {...props} className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 text-slate-200 text-sm outline-none focus:border-blue-500 transition-colors" />
  </div>
);

const Select = ({ label, children, ...props }: any) => (
  <div>
    <label className="block text-xs text-slate-400 mb-1">{label}</label>
    <select {...props} className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 text-slate-200 text-sm outline-none focus:border-blue-500">
      {children}
    </select>
  </div>
);

// ── Coupon Creator ────────────────────────────────────────────────────────
function CouponCreator({ onCreated }: { onCreated: () => void }) {
  const [form, setForm] = useState({
    code: '', name: '', discountType: 'Percentage', discountValue: '10',
    minOrderValue: '0', maxUses: '', usesPerCustomer: '1',
    validTo: new Date(Date.now() + 7*86400000).toISOString().slice(0,16),
  });
  const [saving, setSaving] = useState(false);
  const [msg, setMsg]       = useState('');

  const f = (k: string) => (e: any) => setForm(p => ({...p, [k]: e.target.value}));

  const save = async () => {
    setSaving(true); setMsg('');
    try {
      const res = await fetch(`${API}/mall/admin/promotions/coupons`, {
        method: 'POST', headers: auth(),
        body: JSON.stringify({
          ...form,
          discountValue: +form.discountValue,
          minOrderValue: +form.minOrderValue,
          maxUses: form.maxUses ? +form.maxUses : null,
          usesPerCustomer: +form.usesPerCustomer,
          validTo: new Date(form.validTo).toISOString(),
        }),
      });
      const data = await res.json();
      if (data.success) { setMsg('✅ تم إنشاء الكوبون!'); onCreated(); }
      else setMsg(`❌ ${data.error}`);
    } catch { setMsg('❌ خطأ في الاتصال'); }
    setSaving(false);
  };

  return (
    <div className="card space-y-4">
      <h3 className="font-bold text-slate-200">🎟️ إنشاء كوبون جديد</h3>
      <div className="grid grid-cols-2 gap-3">
        <Input label="الكود *" placeholder="SUMMER25" value={form.code} onChange={f('code')} />
        <Input label="الاسم *" placeholder="خصم الصيف" value={form.name} onChange={f('name')} />
        <Select label="نوع الخصم" value={form.discountType} onChange={f('discountType')}>
          <option value="Percentage">نسبة مئوية %</option>
          <option value="FixedAmount">مبلغ ثابت ج.م</option>
          <option value="FreeDelivery">توصيل مجاني</option>
        </Select>
        <Input label={form.discountType === 'Percentage' ? 'النسبة %' : 'المبلغ ج.م'}
          type="number" value={form.discountValue} onChange={f('discountValue')} />
        <Input label="حد أدنى للطلب ج.م" type="number" value={form.minOrderValue} onChange={f('minOrderValue')} />
        <Input label="عدد الاستخدامات الكلي (فارغ = غير محدود)" type="number" value={form.maxUses} onChange={f('maxUses')} />
        <Input label="مرات الاستخدام لكل عميل" type="number" value={form.usesPerCustomer} onChange={f('usesPerCustomer')} />
        <Input label="صالح حتى" type="datetime-local" value={form.validTo} onChange={f('validTo')} />
      </div>
      {msg && <p className={`text-sm ${msg.startsWith('✅') ? 'text-green-400' : 'text-red-400'}`}>{msg}</p>}
      <button onClick={save} disabled={saving || !form.code || !form.name}
        className="btn-primary w-full disabled:opacity-40">
        {saving ? '⏳ جاري الحفظ...' : 'إنشاء الكوبون'}
      </button>
    </div>
  );
}

// ── Flash Sale Creator ────────────────────────────────────────────────────
function FlashSaleCreator({ onCreated }: { onCreated: () => void }) {
  const now = new Date();
  const [form, setForm] = useState({
    title: '', titleAr: '',
    originalPrice: '', flashPrice: '',
    quantityLimit: '50',
    startsAt: new Date(now.getTime() + 60000).toISOString().slice(0,16),
    endsAt:   new Date(now.getTime() + 24*3600000).toISOString().slice(0,16),
  });
  const [saving, setSaving] = useState(false);
  const [msg, setMsg]       = useState('');

  const f = (k: string) => (e: any) => setForm(p => ({...p, [k]: e.target.value}));

  const discountPct = form.originalPrice && form.flashPrice
    ? ((1 - +form.flashPrice / +form.originalPrice) * 100).toFixed(0) : '0';

  const save = async () => {
    setSaving(true); setMsg('');
    try {
      const res = await fetch(`${API}/mall/admin/promotions/flash-sales`, {
        method: 'POST', headers: auth(),
        body: JSON.stringify({
          ...form,
          originalPrice: +form.originalPrice,
          flashPrice:    +form.flashPrice,
          quantityLimit: +form.quantityLimit,
          startsAt: new Date(form.startsAt).toISOString(),
          endsAt:   new Date(form.endsAt).toISOString(),
        }),
      });
      const data = await res.json();
      if (data.success) { setMsg('✅ تم إنشاء الفلاش سيل!'); onCreated(); }
      else setMsg(`❌ ${data.error}`);
    } catch { setMsg('❌ خطأ في الاتصال'); }
    setSaving(false);
  };

  return (
    <div className="card space-y-4">
      <h3 className="font-bold text-slate-200">⚡ إنشاء Flash Sale</h3>
      <div className="grid grid-cols-2 gap-3">
        <Input label="العنوان (EN)" value={form.title} onChange={f('title')} placeholder="Summer Flash!" />
        <Input label="العنوان (AR)" value={form.titleAr} onChange={f('titleAr')} placeholder="فلاش الصيف!" />
        <Input label="السعر الأصلي ج.م" type="number" value={form.originalPrice} onChange={f('originalPrice')} />
        <div>
          <Input label="سعر الفلاش ج.م" type="number" value={form.flashPrice} onChange={f('flashPrice')} />
          {+discountPct > 0 && (
            <p className="text-xs text-green-400 mt-1">خصم {discountPct}% 🔥</p>
          )}
        </div>
        <Input label="الكمية المتاحة" type="number" value={form.quantityLimit} onChange={f('quantityLimit')} />
        <div />
        <Input label="يبدأ في" type="datetime-local" value={form.startsAt} onChange={f('startsAt')} />
        <Input label="ينتهي في" type="datetime-local" value={form.endsAt} onChange={f('endsAt')} />
      </div>
      {msg && <p className={`text-sm ${msg.startsWith('✅') ? 'text-green-400' : 'text-red-400'}`}>{msg}</p>}
      <button onClick={save} disabled={saving || !form.title || !form.flashPrice}
        className="btn-primary w-full disabled:opacity-40">
        {saving ? '⏳ جاري الحفظ...' : 'إطلاق الفلاش سيل'}
      </button>
    </div>
  );
}

// ── Push Campaign Creator ─────────────────────────────────────────────────
function PushCampaignCreator() {
  const [form, setForm] = useState({
    title: '', titleAr: '', body: '', bodyAr: '',
    target: 'AllCustomers', actionType: '', scheduledAt: '',
  });
  const [saving, setSaving] = useState(false);
  const [msg, setMsg]       = useState('');

  const f = (k: string) => (e: any) => setForm(p => ({...p, [k]: e.target.value}));

  const send = async () => {
    setSaving(true); setMsg('');
    try {
      const res = await fetch(`${API}/mall/admin/campaigns`, {
        method: 'POST', headers: auth(),
        body: JSON.stringify({
          ...form,
          scheduledAt: form.scheduledAt ? new Date(form.scheduledAt).toISOString() : null,
        }),
      });
      const data = await res.json();
      if (data.success) setMsg('✅ تم إرسال الإشعار بنجاح!');
      else setMsg(`❌ ${data.error}`);
    } catch { setMsg('❌ خطأ في الاتصال'); }
    setSaving(false);
  };

  return (
    <div className="card space-y-4">
      <h3 className="font-bold text-slate-200">🔔 إرسال إشعار جماعي</h3>
      <div className="grid grid-cols-2 gap-3">
        <Input label="العنوان (EN)" value={form.title} onChange={f('title')} placeholder="Special Offer!" />
        <Input label="العنوان (AR)" value={form.titleAr} onChange={f('titleAr')} placeholder="عرض خاص!" />
        <div className="col-span-2">
          <label className="block text-xs text-slate-400 mb-1">النص (EN)</label>
          <textarea value={form.body} onChange={f('body')}
            className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 text-slate-200 text-sm outline-none focus:border-blue-500 h-20 resize-none"
            placeholder="Today only — 30% off all orders..." />
        </div>
        <div className="col-span-2">
          <label className="block text-xs text-slate-400 mb-1">النص (AR)</label>
          <textarea value={form.bodyAr} onChange={f('bodyAr')}
            className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 text-slate-200 text-sm outline-none focus:border-blue-500 h-20 resize-none"
            placeholder="اليوم فقط — خصم 30% على جميع الطلبات..." />
        </div>
        <Select label="الجمهور المستهدف" value={form.target} onChange={f('target')}>
          <option value="AllCustomers">جميع العملاء</option>
          <option value="TierGold">Gold فقط 🥇</option>
          <option value="TierSilver">Silver فقط 🥈</option>
          <option value="TierBronze">Bronze فقط 🥉</option>
          <option value="InMallZone">داخل المول الآن</option>
        </Select>
        <Input label="جدولة (اختياري)" type="datetime-local" value={form.scheduledAt} onChange={f('scheduledAt')} />
      </div>

      {/* Preview */}
      {(form.titleAr || form.bodyAr) && (
        <div className="border border-slate-700 rounded-xl p-3 bg-slate-900">
          <p className="text-xs text-slate-500 mb-2">👁️ معاينة الإشعار</p>
          <div className="flex items-start gap-2">
            <div className="w-8 h-8 bg-blue-600 rounded-lg flex items-center justify-center text-sm">🛍️</div>
            <div>
              <p className="text-slate-200 text-sm font-bold">{form.titleAr || form.title}</p>
              <p className="text-slate-400 text-xs mt-0.5">{form.bodyAr || form.body}</p>
            </div>
          </div>
        </div>
      )}

      {msg && <p className={`text-sm ${msg.startsWith('✅') ? 'text-green-400' : 'text-red-400'}`}>{msg}</p>}
      <button onClick={send} disabled={saving || !form.title || !form.body}
        className="btn-primary w-full disabled:opacity-40">
        {saving ? '⏳ جاري الإرسال...' : (form.scheduledAt ? '📅 جدولة الإشعار' : '🚀 إرسال الآن')}
      </button>
    </div>
  );
}

// ── Main ──────────────────────────────────────────────────────────────────
export default function PromotionsAdminPage() {
  const [tab, setTab] = useState<Tab>('coupons');
  const [reload, setReload] = useState(0);

  return (
    <div className="min-h-screen p-6 max-w-5xl mx-auto" dir="rtl">
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-slate-100">🎯 إدارة العروض والإشعارات</h1>
        <p className="text-slate-400 text-sm mt-1">أنشئ كوبونات، عروض محدودة، وأرسل إشعارات جماعية</p>
      </div>

      {/* Tabs */}
      <div className="flex gap-2 mb-6 border-b border-slate-800">
        {([['coupons','🎟️ الكوبونات'],['flash','⚡ Flash Sales'],['push','🔔 الإشعارات']] as const).map(([id, label]) => (
          <button key={id} onClick={() => setTab(id)}
            className={`px-4 py-2 text-sm font-semibold border-b-2 transition-colors ${
              tab===id ? 'border-blue-500 text-blue-400' : 'border-transparent text-slate-400'}`}>
            {label}
          </button>
        ))}
      </div>

      {tab === 'coupons' && <CouponCreator onCreated={() => setReload(r => r+1)} />}
      {tab === 'flash'   && <FlashSaleCreator onCreated={() => setReload(r => r+1)} />}
      {tab === 'push'    && <PushCampaignCreator />}

      <style jsx global>{`
        .card { background:#0f172a; border:1px solid #1e293b; border-radius:14px; padding:20px; }
        .btn-primary { background:#3b82f6; color:white; border:none; border-radius:10px;
          padding:12px 20px; font-weight:700; cursor:pointer; font-size:14px; }
        .btn-primary:hover { background:#2563eb; }
      `}</style>
    </div>
  );
}

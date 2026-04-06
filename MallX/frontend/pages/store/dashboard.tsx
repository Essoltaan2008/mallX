import { useEffect, useState, useCallback } from 'react';

const API = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000/api';
const auth = () => ({ Authorization: `Bearer ${localStorage.getItem('mx_token')}` });

type OrderStatus = 'Placed'|'Confirmed'|'Preparing'|'Ready'|'Cancelled';
interface OrderItem { productName:string; quantity:number; unitPrice:number; total:number; notes?:string }
interface IncomingOrder {
  id:string; orderNumber:string; status:OrderStatus; customerName:string;
  customerPhone?:string; fulfillmentType:string; total:number;
  items:OrderItem[]; placedAt:string; notes?:string;
}
interface Financials {
  thisMonthRevenue:number; thisMonthCommission:number;
  thisMonthNet:number; pendingSettlement:number;
}

const STATUS_MAP: Record<OrderStatus, {label:string; color:string; next?:OrderStatus; action?:string}> = {
  Placed:    { label:'جديد',          color:'#f59e0b', next:'Confirmed', action:'قبول الطلب' },
  Confirmed: { label:'مؤكد',          color:'#3b82f6', next:'Preparing', action:'بدء التحضير' },
  Preparing: { label:'قيد التحضير',   color:'#8b5cf6', next:'Ready',    action:'جاهز للاستلام' },
  Ready:     { label:'جاهز',          color:'#10b981'  },
  Cancelled: { label:'ملغى',          color:'#ef4444'  },
};
const fmt = (n:number) => n?.toLocaleString('ar-EG') ?? '0';

// ─── Order Card ───────────────────────────────────────────────────────────
function OrderCard({ order, onUpdate }: { order:IncomingOrder; onUpdate:(id:string, s:string)=>void }) {
  const [open, setOpen] = useState(false);
  const [updating, setUpdating] = useState(false);
  const meta = STATUS_MAP[order.status];

  const handleUpdate = async (nextStatus: string) => {
    setUpdating(true);
    try {
      await fetch(`${API}/mall/store/orders/${order.id}/status`, {
        method:'PATCH', headers:{'Content-Type':'application/json', ...auth()},
        body: JSON.stringify({ status: nextStatus, note: `تحديث من لوحة التحكم` }),
      });
      onUpdate(order.id, nextStatus);
    } catch(e) { console.error(e); }
    setUpdating(false);
  };

  return (
    <div className="card mb-3" style={{borderRight:`3px solid ${meta.color}`}}>
      {/* Header */}
      <div className="flex items-start justify-between gap-2 flex-wrap">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <span className="font-bold text-slate-100">{order.orderNumber}</span>
            <span className="badge text-xs font-semibold px-2 py-0.5 rounded-full"
              style={{background:`${meta.color}22`, color:meta.color}}>
              {meta.label}
            </span>
            <span className="text-xs text-slate-500 px-2 py-0.5 rounded bg-slate-800">
              {order.fulfillmentType === 'Delivery' ? '🚗 توصيل' : '🏪 استلام'}
            </span>
          </div>
          <p className="text-sm text-slate-400">
            👤 {order.customerName}
            {order.customerPhone && <span className="ml-2 text-slate-500">— {order.customerPhone}</span>}
          </p>
          <p className="text-xs text-slate-500 mt-0.5">
            {new Date(order.placedAt).toLocaleString('ar-EG')}
          </p>
        </div>
        <div className="text-left">
          <p className="text-lg font-bold text-blue-400">{fmt(order.total)} ج.م</p>
          <p className="text-xs text-slate-400">{order.items.length} صنف</p>
        </div>
      </div>

      {/* Items toggle */}
      <button onClick={() => setOpen(!open)}
        className="text-xs text-slate-400 mt-2 hover:text-slate-300 flex items-center gap-1">
        {open ? '▲ إخفاء الأصناف' : '▼ عرض الأصناف'}
      </button>

      {open && (
        <div className="mt-3 space-y-1 border-t border-slate-700 pt-3">
          {order.items.map((item, i) => (
            <div key={i} className="flex justify-between text-sm">
              <span className="text-slate-300">
                {item.quantity}× {item.productName}
                {item.notes && <span className="text-slate-500 text-xs"> ({item.notes})</span>}
              </span>
              <span className="text-slate-400">{fmt(item.total)} ج.م</span>
            </div>
          ))}
          {order.notes && (
            <p className="text-xs text-yellow-400 mt-2 bg-yellow-400/10 rounded px-2 py-1">
              📝 ملاحظة: {order.notes}
            </p>
          )}
        </div>
      )}

      {/* Action button */}
      {meta.next && meta.action && (
        <button
          onClick={() => handleUpdate(meta.next!)}
          disabled={updating}
          className="mt-3 w-full py-2 rounded-lg text-sm font-semibold transition-all"
          style={{background:`${meta.color}22`, color:meta.color,
            border:`1px solid ${meta.color}44`,
            opacity: updating ? 0.6 : 1}}>
          {updating ? '⏳ جاري التحديث...' : `✓ ${meta.action}`}
        </button>
      )}
      {order.status === 'Placed' && (
        <button
          onClick={() => handleUpdate('Cancelled')}
          disabled={updating}
          className="mt-1 w-full py-1.5 rounded-lg text-xs text-red-400 hover:bg-red-400/10 transition-colors">
          ✗ رفض الطلب
        </button>
      )}
    </div>
  );
}

// ─── Main ─────────────────────────────────────────────────────────────────
export default function StoreDashboard() {
  const [orders, setOrders] = useState<IncomingOrder[]>([]);
  const [fin, setFin]       = useState<Financials|null>(null);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter]   = useState<string>('all');
  const [tab, setTab]         = useState<'orders'|'financials'>('orders');
  const [newCount, setNewCount] = useState(0);

  const load = useCallback(async () => {
    try {
      const [o, f] = await Promise.all([
        fetch(`${API}/mall/store/orders/incoming`, { headers: auth() }).then(r => r.json()),
        fetch(`${API}/mall/store/financials`,      { headers: auth() }).then(r => r.json()),
      ]);
      const loaded = o.data as IncomingOrder[];
      setOrders(loaded);
      setNewCount(loaded.filter((x:IncomingOrder) => x.status === 'Placed').length);
      setFin(f.data);
    } catch(e) { console.error(e); }
    setLoading(false);
  }, []);

  useEffect(() => {
    load();
    // Auto-refresh every 30s
    const t = setInterval(load, 30_000);
    return () => clearInterval(t);
  }, [load]);

  const handleUpdate = (id: string, status: string) => {
    setOrders(prev => prev.map(o =>
      o.id === id ? {...o, status: status as OrderStatus} : o));
    setNewCount(prev => status !== 'Placed' ? Math.max(0, prev-1) : prev);
  };

  const filtered = filter === 'all'
    ? orders
    : orders.filter(o => o.status === filter);

  const grouped = {
    Placed:    orders.filter(o=>o.status==='Placed'),
    Confirmed: orders.filter(o=>o.status==='Confirmed'),
    Preparing: orders.filter(o=>o.status==='Preparing'),
    Ready:     orders.filter(o=>o.status==='Ready'),
  };

  if (loading) return (
    <div className="min-h-screen flex items-center justify-center bg-slate-950">
      <div className="text-blue-400 animate-pulse text-lg">⏳ تحميل الطلبات...</div>
    </div>
  );

  return (
    <div className="min-h-screen bg-slate-950 p-4" dir="rtl">

      {/* Header */}
      <div className="flex items-center justify-between mb-6 flex-wrap gap-3">
        <div>
          <h1 className="text-xl font-bold text-slate-100">🏪 لوحة تحكم المحل</h1>
          <p className="text-slate-400 text-sm">آخر تحديث: {new Date().toLocaleTimeString('ar-EG')}</p>
        </div>
        <div className="flex gap-2">
          {newCount > 0 && (
            <span className="bg-red-500 text-white text-sm font-bold px-3 py-1 rounded-full animate-pulse">
              🔔 {newCount} طلب جديد
            </span>
          )}
          <button onClick={load}
            className="text-sm px-3 py-1.5 rounded-lg border border-slate-700 text-slate-300 hover:bg-slate-800">
            🔄 تحديث
          </button>
        </div>
      </div>

      {/* Quick Stats */}
      <div className="grid grid-cols-4 gap-3 mb-6">
        {Object.entries(grouped).map(([status, items]) => {
          const meta = STATUS_MAP[status as OrderStatus];
          return (
            <div key={status} className="card text-center cursor-pointer hover:border-slate-600 transition-colors"
              onClick={() => setFilter(filter===status ? 'all' : status)}
              style={{borderColor: filter===status ? meta.color : undefined}}>
              <p className="text-2xl font-bold" style={{color: meta.color}}>{items.length}</p>
              <p className="text-xs text-slate-400 mt-1">{meta.label}</p>
            </div>
          );
        })}
      </div>

      {/* Tabs */}
      <div className="flex gap-2 mb-4 border-b border-slate-800 pb-0">
        {([['orders','📦 الطلبات'],['financials','💰 المالية']] as const).map(([id,label]) => (
          <button key={id} onClick={() => setTab(id)}
            className={`px-4 py-2 text-sm font-semibold border-b-2 transition-colors ${
              tab===id ? 'border-blue-500 text-blue-400' : 'border-transparent text-slate-400'
            }`}>
            {label}
          </button>
        ))}
      </div>

      {/* Orders Tab */}
      {tab === 'orders' && (
        <>
          {/* Filter pills */}
          <div className="flex gap-2 mb-4 flex-wrap">
            {(['all','Placed','Confirmed','Preparing','Ready'] as const).map(s => (
              <button key={s} onClick={() => setFilter(s)}
                className={`text-xs px-3 py-1 rounded-full transition-colors ${
                  filter===s
                    ? 'bg-blue-600 text-white'
                    : 'bg-slate-800 text-slate-400 hover:bg-slate-700'
                }`}>
                {s==='all' ? 'الكل' : STATUS_MAP[s]?.label}
                <span className="mr-1 opacity-60">
                  ({s==='all' ? orders.length : orders.filter(o=>o.status===s).length})
                </span>
              </button>
            ))}
          </div>

          {filtered.length === 0
            ? <div className="text-center text-slate-500 py-12">لا توجد طلبات في هذه الحالة</div>
            : filtered.map(o => (
                <OrderCard key={o.id} order={o} onUpdate={handleUpdate} />
              ))
          }
        </>
      )}

      {/* Financials Tab */}
      {tab === 'financials' && fin && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            {[
              { label:'إيرادات الشهر',   val:`${fmt(fin.thisMonthRevenue)} ج.م`,   color:'#3b82f6' },
              { label:'العمولة المستحقة', val:`${fmt(fin.thisMonthCommission)} ج.م`, color:'#ef4444' },
              { label:'صافي المحل',       val:`${fmt(fin.thisMonthNet)} ج.م`,        color:'#10b981' },
              { label:'في انتظار التسوية',val:`${fmt(fin.pendingSettlement)} ج.م`,   color:'#f59e0b' },
            ].map(({label,val,color}) => (
              <div key={label} className="card text-center">
                <p className="text-slate-400 text-xs mb-1">{label}</p>
                <p className="text-xl font-bold" style={{color}}>{val}</p>
              </div>
            ))}
          </div>
          <div className="card">
            <h3 className="font-semibold text-slate-200 mb-3">💡 ملاحظة</h3>
            <p className="text-slate-400 text-sm leading-relaxed">
              يتم احتساب العمولة تلقائياً على كل طلب مكتمل. التسوية الشهرية يجمعها مدير المول
              ويحولها إلى حسابك في نهاية كل شهر.
            </p>
          </div>
        </div>
      )}

      <style jsx global>{`
        .card { background:#0f172a; border:1px solid #1e293b; border-radius:12px; padding:16px; }
      `}</style>
    </div>
  );
}

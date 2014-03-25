using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TSystems.RELOAD.Util
{
    public class ThreadSafeDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private readonly object syncRoot = new object();
        private Dictionary<TKey, TValue> innerDictionary = new Dictionary<TKey, TValue>();

        public void Add(TKey key, TValue value)
        {
            lock (this.syncRoot)
            {
                this.innerDictionary.Add(key, value);
            }
        }

        public bool ContainsKey(TKey key)
        {
            lock (this.syncRoot)
            {
                return this.innerDictionary.ContainsKey(key);
            }
        }

        public ICollection<TKey> Keys
        {
            get { throw new NotImplementedException(); }
        }

        public bool Remove(TKey key)
        {
            lock (this.syncRoot)
            {
                return this.innerDictionary.Remove(key);
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock(syncRoot)
            {
                return this.innerDictionary.TryGetValue(key, out value);
            }
        }

        public ICollection<TValue> Values
        {
            get { throw new NotImplementedException(); }
        }

        public TValue this[TKey key]
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.innerDictionary[key];
                }
            }
            set
            {
                lock (this.syncRoot)
                {
                    this.innerDictionary[key] = value;
                 }
            }
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public int Count
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.innerDictionary.Count;
                }
            }
        }

        public bool IsReadOnly
        {
            get { throw new NotImplementedException(); }
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            lock(this.syncRoot)
            {
                return this.innerDictionary.GetEnumerator();
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}

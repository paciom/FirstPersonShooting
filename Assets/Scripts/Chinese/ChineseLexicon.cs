using System.Collections.Generic;
using System.Text;

/// <summary>
/// The word list Chinese Quest asks from: kid-sized HSK 1–2 vocabulary, split
/// into themed decks so a round's three wrong answers can be drawn from the
/// SAME deck as the right one. Same-theme distractors are what make the
/// question a question — four random words from four themes can be answered
/// off the topic alone, without reading the character.
///
/// This file is the single source of truth for three things that must agree:
/// the characters the game shows, the glyphs Tools/subsetfont.py packs into
/// the shipped font, and the audio ids Tools/chinesevoice.ps1 bakes
/// pronunciation clips under. Both tools PARSE this file, so adding a word
/// here and re-running them is the whole workflow — see
/// Assets/Resources/Chinese/README.md.
///
/// Saved UTF-8 with a BOM on purpose: without one, a C# compiler invoked
/// outside Unity may read the file in the machine's ANSI codepage and turn
/// every character into mojibake that still compiles.
/// </summary>
public static class ChineseLexicon
{
    /// <summary>One vocabulary item: the character(s), how it sounds, what it means.</summary>
    public struct Word
    {
        public readonly string hanzi;
        public readonly string pinyin;
        public readonly string english;

        public Word(string hanzi, string pinyin, string english)
        {
            this.hanzi = hanzi;
            this.pinyin = pinyin;
            this.english = english;
        }

        public bool IsValid => !string.IsNullOrEmpty(hanzi);

        /// <summary>
        /// The name of this word's baked pronunciation clip, derived from the
        /// pinyin rather than stored — a hand-written id is one more field to
        /// get wrong, and the tone-marked pinyin is already unique.
        /// </summary>
        public string AudioId => ToneSlug(pinyin);
    }

    /// <summary>A themed set of words — one round's question and its three decoys.</summary>
    public class Deck
    {
        public readonly string title;    // shown in caps on the deck card
        public readonly string hanzi;    // the same name in Chinese, under it
        public readonly Word[] words;

        public Deck(string title, string hanzi, Word[] words)
        {
            this.title = title;
            this.hanzi = hanzi;
            this.words = words;
        }

        public int Count => words.Length;
    }

    static Word W(string hanzi, string pinyin, string english) => new Word(hanzi, pinyin, english);

    // ------------------------------------------------------------ the decks
    //
    // Four words minimum per deck — a round needs one answer and three
    // decoys — but every deck here runs 10+ so the same four rarely repeat.

    public static readonly Deck[] Decks =
    {
        new Deck("ANIMALS", "动物", new[]
        {
            W("猫", "māo", "cat"),
            W("狗", "gǒu", "dog"),
            W("鸟", "niǎo", "bird"),
            W("鱼", "yú", "fish"),
            W("马", "mǎ", "horse"),
            W("牛", "niú", "cow"),
            W("羊", "yáng", "sheep"),
            W("猪", "zhū", "pig"),
            W("鸡", "jī", "chicken"),
            W("鸭", "yā", "duck"),
            W("虎", "hǔ", "tiger"),
            W("龙", "lóng", "dragon"),
            W("兔子", "tù zi", "rabbit"),
            W("熊猫", "xióng māo", "panda"),
            W("大象", "dà xiàng", "elephant"),
            W("猴子", "hóu zi", "monkey"),
        }),

        new Deck("NUMBERS", "数字", new[]
        {
            W("零", "líng", "zero"),
            W("一", "yī", "one"),
            W("二", "èr", "two"),
            W("三", "sān", "three"),
            W("四", "sì", "four"),
            W("五", "wǔ", "five"),
            W("六", "liù", "six"),
            W("七", "qī", "seven"),
            W("八", "bā", "eight"),
            W("九", "jiǔ", "nine"),
            W("十", "shí", "ten"),
            W("百", "bǎi", "hundred"),
            W("千", "qiān", "thousand"),
            W("半", "bàn", "half"),
        }),

        new Deck("COLORS", "颜色", new[]
        {
            W("红", "hóng", "red"),
            W("蓝", "lán", "blue"),
            W("黄", "huáng", "yellow"),
            W("绿", "lǜ", "green"),
            W("白", "bái", "white"),
            W("黑", "hēi", "black"),
            W("紫", "zǐ", "purple"),
            W("橙", "chéng", "orange"),
            W("粉", "fěn", "pink"),
            W("灰", "huī", "gray"),
            W("金", "jīn", "gold"),
            W("银", "yín", "silver"),
        }),

        new Deck("FAMILY", "家人", new[]
        {
            W("妈妈", "mā ma", "mother"),
            W("爸爸", "bà ba", "father"),
            W("哥哥", "gē ge", "older brother"),
            W("姐姐", "jiě jie", "older sister"),
            W("弟弟", "dì di", "younger brother"),
            W("妹妹", "mèi mei", "younger sister"),
            W("爷爷", "yé ye", "grandpa"),
            W("奶奶", "nǎi nai", "grandma"),
            W("家", "jiā", "home"),
            W("朋友", "péng you", "friend"),
            W("老师", "lǎo shī", "teacher"),
            W("学生", "xué sheng", "student"),
        }),

        new Deck("FOOD", "食物", new[]
        {
            W("水", "shuǐ", "water"),
            W("米饭", "mǐ fàn", "rice"),
            W("面条", "miàn tiáo", "noodles"),
            W("面包", "miàn bāo", "bread"),
            W("鸡蛋", "jī dàn", "egg"),
            W("苹果", "píng guǒ", "apple"),
            W("香蕉", "xiāng jiāo", "banana"),
            W("西瓜", "xī guā", "watermelon"),
            W("牛奶", "niú nǎi", "milk"),
            W("茶", "chá", "tea"),
            W("糖", "táng", "candy"),
            W("肉", "ròu", "meat"),
            W("菜", "cài", "vegetable"),
            W("饺子", "jiǎo zi", "dumpling"),
        }),

        new Deck("BODY", "身体", new[]
        {
            W("头", "tóu", "head"),
            W("手", "shǒu", "hand"),
            W("脚", "jiǎo", "foot"),
            W("眼睛", "yǎn jing", "eye"),
            W("耳朵", "ěr duo", "ear"),
            W("嘴", "zuǐ", "mouth"),
            W("鼻子", "bí zi", "nose"),
            W("心", "xīn", "heart"),
            W("牙", "yá", "tooth"),
            W("腿", "tuǐ", "leg"),
        }),

        new Deck("NATURE", "大自然", new[]
        {
            W("天", "tiān", "sky"),
            W("太阳", "tài yáng", "sun"),
            W("月亮", "yuè liang", "moon"),
            W("星星", "xīng xing", "star"),
            W("山", "shān", "mountain"),
            W("河", "hé", "river"),
            W("火", "huǒ", "fire"),
            W("雨", "yǔ", "rain"),
            W("雪", "xuě", "snow"),
            W("风", "fēng", "wind"),
            W("树", "shù", "tree"),
            W("花", "huā", "flower"),
            W("石头", "shí tou", "stone"),
            W("云", "yún", "cloud"),
        }),

        new Deck("ACTIONS", "动作", new[]
        {
            W("走", "zǒu", "walk"),
            W("跑", "pǎo", "run"),
            W("跳", "tiào", "jump"),
            W("吃", "chī", "eat"),
            W("喝", "hē", "drink"),
            W("看", "kàn", "look"),
            W("听", "tīng", "listen"),
            W("说", "shuō", "speak"),
            W("读", "dú", "read"),
            W("写", "xiě", "write"),
            W("笑", "xiào", "laugh"),
            W("哭", "kū", "cry"),
            W("睡", "shuì", "sleep"),
            W("飞", "fēi", "fly"),
        }),

        new Deck("THINGS", "东西", new[]
        {
            W("书", "shū", "book"),
            W("笔", "bǐ", "pen"),
            W("纸", "zhǐ", "paper"),
            W("门", "mén", "door"),
            W("窗", "chuāng", "window"),
            W("桌子", "zhuō zi", "table"),
            W("椅子", "yǐ zi", "chair"),
            W("车", "chē", "car"),
            W("船", "chuán", "boat"),
            W("电脑", "diàn nǎo", "computer"),
            W("手机", "shǒu jī", "phone"),
            W("钱", "qián", "money"),
        }),

        new Deck("OPPOSITES", "反义词", new[]
        {
            W("大", "dà", "big"),
            W("小", "xiǎo", "small"),
            W("好", "hǎo", "good"),
            W("坏", "huài", "bad"),
            W("多", "duō", "many"),
            W("少", "shǎo", "few"),
            W("高", "gāo", "tall"),
            W("矮", "ǎi", "short"),
            W("新", "xīn", "new"),
            W("旧", "jiù", "old"),
            W("快", "kuài", "fast"),
            W("慢", "màn", "slow"),
            W("热", "rè", "hot"),
            W("冷", "lěng", "cold"),
            W("上", "shàng", "up"),
            W("下", "xià", "down"),
        }),

        // The home deck: this game's own words, so the arena the player is
        // standing in is also the vocabulary they are learning.
        new Deck("ROBOTS", "机器人", new[]
        {
            W("机器人", "jī qì rén", "robot"),
            W("光", "guāng", "light"),
            W("激光", "jī guāng", "laser"),
            W("火箭", "huǒ jiàn", "rocket"),
            W("星球", "xīng qiú", "planet"),
            W("太空", "tài kōng", "space"),
            W("电", "diàn", "electricity"),
            W("能量", "néng liàng", "energy"),
            W("盾", "dùn", "shield"),
            W("剑", "jiàn", "sword"),
            W("战士", "zhàn shì", "warrior"),
            W("英雄", "yīng xióng", "hero"),
        }),
    };

    static Deck _everything;

    /// <summary>
    /// Every deck poured into one — the hardest setting, since a round's
    /// decoys may then come from any theme at all. Built once on demand.
    /// </summary>
    public static Deck Everything
    {
        get
        {
            if (_everything == null)
            {
                var all = new List<Word>();
                foreach (var deck in Decks)
                    all.AddRange(deck.words);
                _everything = new Deck("EVERYTHING", "全部", all.ToArray());
            }
            return _everything;
        }
    }

    /// <summary>The decks in menu order: the themed ones, then the mixed bag.</summary>
    public static Deck DeckAt(int index)
    {
        if (index < 0 || index >= Decks.Length)
            return Everything;
        return Decks[index];
    }

    public static int MenuCount => Decks.Length + 1;

    // ------------------------------------------------------- pinyin → ascii

    // Tone-marked vowel → (plain letter, tone number). ü is spelled v, the
    // convention every pinyin input method already uses.
    static readonly Dictionary<char, string> ToneMap = new Dictionary<char, string>
    {
        ['ā'] = "a1", ['á'] = "a2", ['ǎ'] = "a3", ['à'] = "a4",
        ['ō'] = "o1", ['ó'] = "o2", ['ǒ'] = "o3", ['ò'] = "o4",
        ['ē'] = "e1", ['é'] = "e2", ['ě'] = "e3", ['è'] = "e4",
        ['ī'] = "i1", ['í'] = "i2", ['ǐ'] = "i3", ['ì'] = "i4",
        ['ū'] = "u1", ['ú'] = "u2", ['ǔ'] = "u3", ['ù'] = "u4",
        ['ǖ'] = "v1", ['ǘ'] = "v2", ['ǚ'] = "v3", ['ǜ'] = "v4",
        ['ü'] = "v0",
    };

    /// <summary>
    /// "xióng māo" → "xiong2_mao1": an ASCII filename that survives every
    /// filesystem and asset importer, and still reads back as the word.
    ///
    /// Tools/chinesevoice.ps1 reimplements this rule exactly; changing it
    /// here without changing it there orphans every baked clip.
    /// </summary>
    public static string ToneSlug(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return "";

        var slug = new StringBuilder();
        foreach (string syllable in pinyin.Split(' '))
        {
            if (syllable.Length == 0)
                continue;
            if (slug.Length > 0)
                slug.Append('_');

            // The tone mark can sit on any vowel of the syllable, but the
            // digit always lands at the end — so the letters are collected
            // first and the tone is appended after them.
            var letters = new StringBuilder();
            char tone = '0';
            foreach (char c in syllable)
            {
                if (ToneMap.TryGetValue(c, out string mapped))
                {
                    letters.Append(mapped[0]);
                    if (mapped[1] != '0')
                        tone = mapped[1];
                }
                else
                {
                    letters.Append(char.ToLowerInvariant(c));
                }
            }
            slug.Append(letters).Append(tone);
        }
        return slug.ToString();
    }
}

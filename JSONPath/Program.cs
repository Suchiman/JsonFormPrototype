using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace JSONPath
{
    class Program
    {
        static void Main(string[] args)
        {
            Example1();
            //Example2();
            Example3();
            Example4();
            Example5();
            Example6();
            Example7();
            Example8();
            Example10();
        }

        static void Example1()
        {
            FormDataSet set = new FormDataSet();
            set.Append("name", "Bender", "text");
            set.Append("hind", "Bitable", "checkbox");
            set.Append("shiny", "true", "text");
            var result = Encoding.UTF8.GetString(ApplicationJsonEncode(set));
            Debug.Assert("{\"name\":\"Bender\",\"hind\":\"Bitable\",\"shiny\":\"true\"}" == result);
        }

        static void Example2()
        {
            FormDataSet set = new FormDataSet();
            set.Append("bottle-on-wall", "1", "number");
            set.Append("bottle-on-wall", "2", "number");
            set.Append("bottle-on-wall", "3", "number");
            var result = Encoding.UTF8.GetString(ApplicationJsonEncode(set));
            Debug.Assert("{\"bottle-on-wall\":[1,2,3]}" == result);
        }

        static void Example3()
        {
            FormDataSet set = new FormDataSet();
            set.Append("pet[species]", "Dahut", "text");
            set.Append("pet[name]", "Hypatia", "text");
            set.Append("kids[1]", "Thelma", "text");
            set.Append("kids[0]", "Ashley", "text");
            var result = Encoding.UTF8.GetString(ApplicationJsonEncode(set));
            Debug.Assert("{\"pet\":{\"species\":\"Dahut\",\"name\":\"Hypatia\"},\"kids\":[\"Ashley\",\"Thelma\"]}" == result);
        }

        static void Example4()
        {
            FormDataSet set = new FormDataSet();
            set.Append("hearbeat[0]", "thunk", "text");
            set.Append("hearbeat[2]", "thunk", "text");
            var result = Encoding.UTF8.GetString(ApplicationJsonEncode(set));
            Debug.Assert("{\"hearbeat\":[\"thunk\",null,\"thunk\"]}" == result);
        }

        static void Example5()
        {
            FormDataSet set = new FormDataSet();
            set.Append("pet[0][species]", "Dahut", "text");
            set.Append("pet[0][name]", "Hypatia", "text");
            set.Append("pet[1][species]", "Felis Stultus", "text");
            set.Append("pet[1][name]", "Billie", "text");
            var result = Encoding.UTF8.GetString(ApplicationJsonEncode(set));
            Debug.Assert("{\"pet\":[{\"species\":\"Dahut\",\"name\":\"Hypatia\"},{\"species\":\"Felis Stultus\",\"name\":\"Billie\"}]}" == result);
        }

        static void Example6()
        {
            FormDataSet set = new FormDataSet();
            set.Append("wow[such][deep][3][much][power][!]", "Amaze", "text");
            var result = Encoding.UTF8.GetString(ApplicationJsonEncode(set));
            Debug.Assert("{\"wow\":{\"such\":{\"deep\":[null,null,null,{\"much\":{\"power\":{\"!\":\"Amaze\"}}}]}}}" == result);
        }

        static void Example7()
        {
            FormDataSet set = new FormDataSet();
            set.Append("mix", "scalar", "text");
            set.Append("mix[0]", "array 1", "text");
            set.Append("mix[2]", "array 2", "text");
            set.Append("mix[key]", "key key", "text");
            set.Append("mix[car]", "car key", "text");
            var result = Encoding.UTF8.GetString(ApplicationJsonEncode(set));
            Debug.Assert("{\"mix\":{\"\":\"scalar\",\"0\":\"array 1\",\"2\":\"array 2\",\"key\":\"key key\",\"car\":\"car key\"}}" == result);
        }

        static void Example8()
        {
            FormDataSet set = new FormDataSet();
            set.Append("highlander[]", "one", "text");
            var result = Encoding.UTF8.GetString(ApplicationJsonEncode(set));
            Debug.Assert("{\"highlander\":[\"one\"]}" == result);
        }

        static void Example10()
        {
            FormDataSet set = new FormDataSet();
            set.Append("error[good]", "BOOM!", "text");
            set.Append("error[bad", "BOOM BOOM!", "text");
            var result = Encoding.UTF8.GetString(ApplicationJsonEncode(set));
            Debug.Assert("{\"error\":{\"good\":\"BOOM!\"},\"error[bad\":\"BOOM BOOM!\"}" == result);
        }

        private static byte[] ApplicationJsonEncode(FormDataSet formDataSet)
        {
            //1. Let resulting object be a new Object.
            var resultingObject = new JsonObject();

            //2. For each entry in the form data set, perform these substeps:
            foreach (var entry in formDataSet._entries)
            {
                //2.1. If the entry's type is file, set the is file flag.
                bool isFile = entry is FileDataSetEntry;

                TextDataSetEntry text = entry as TextDataSetEntry;
                FileDataSetEntry file = entry as FileDataSetEntry;
                JsonValue entryValue;
                if (text != null)
                {
                    entryValue = new JsonValue(text.Value);
                }
                else
                {
                    var stream = file.Value.Body;
                    MemoryStream ms = stream as MemoryStream;
                    if (ms == null)
                    {
                        ms = new MemoryStream();
                        stream.CopyTo(ms);
                    }
                    entryValue = new JsonValue(Convert.ToBase64String(ms.ToArray()));
                }

                //2.2. Let steps be the result of running the steps to parse a JSON encoding path on the entry's name. 
                List<Step> steps = ParseJSONPath(entry.Name);

                //2.3. Let context be set to the value of resulting object.
                JsonElement context = resultingObject;

                //2.4. For each step in the list of steps, run the following subsubsteps:
                foreach (var step in steps)
                {
                    //2.4.1. Let the current value be the value obtained by getting the step's key from the current context. 
                    JsonElement currentValue = context[step.Key];

                    //2.4.2. Run the steps to set a JSON encoding value with the current context, the step, the current value, the entry's value, and the is file flag. 
                    //2.4.3. Update context to be the value returned by the steps to set a JSON encoding value ran above.
                    context = JsonEncodeValue(context, step, currentValue, entryValue, isFile);
                }
            }

            //3. Let result be the value returned from calling the stringify operation with resulting object as its first parameter and the two remaining parameters left undefined.
            string result = Stringify(resultingObject);

            //4. Encode result as UTF - 8 and return the resulting byte stream. 
            return Encoding.UTF8.GetBytes(result);
        }

        private static string Stringify(JsonElement resultingObject)
        {
            return resultingObject.ToString();
        }

        private static JsonElement JsonEncodeValue(JsonElement context, Step step, JsonElement currentValue, JsonElement entryValue, bool isFile)
        {
            //1. Let context be the context this algorithm is called with.
            //2. Let step be the step of the path this algorithm is called with.
            //3. Let current value be the current value this algorithm is called with.
            //4. Let entry value be the entry value this algorithm is called with.
            //5. Let is file be the is file flag this algorithm is called with.

            //6. If is file is set then replace entry value with an Object have its "name" property set to the file's name, its "type" property set to the file's type, and its "body" property set to the Base64 encoding of the file's body. [RFC2045]  
            if (isFile)
            {
                JsonObject file = new JsonObject();
                file["name"] = new JsonValue("dummy.txt");
                file["type"] = new JsonValue("txt/txt");
                //file["body"] = new JsonValue(Convert.ToBase64String((byte[])file));
                //var file = (object)entryValue; //cast to underlying file type
                entryValue = file;
            }

            //7. If step has its last flag set, run the following substeps:
            if (step.Last)
            {
                //7.1. If current value is undefined, run the following subsubsteps:
                if (currentValue == null) //undefined
                {
                    //7.1.1. If step's append flag is set, set the context's property named by the step's key to a new Array containing entry value as its only member. 
                    if (step.Append)
                    {
                        var arr = new JsonArray();
                        arr.Elements.Add(entryValue);
                        context[step.Key] = arr;
                    }
                    //7.1.2. Otherwise, set the context's property named by the step's key to entry value. 
                    else
                    {
                        context[step.Key] = entryValue;
                    }
                }
                //7.2. Else if current value is an Array, then get the context's property named by the step's key and push entry value onto it. 
                else if (currentValue is JsonArray)
                {
                    (context[step.Key] as JsonArray).Elements.Add(entryValue);
                }
                //7.3. Else if current value is an Object and the is file flag is not set, then run the steps to set a JSON encoding value with
                //context set to the current value;
                //a step with its type set to "object", its key set to the empty string, and its last flag set;
                //current value set to the current value's property named by the empty string;
                //the entry value;
                //and the is file flag.
                //Return the result. 
                else if (currentValue is JsonObject && !isFile)
                {
                    return JsonEncodeValue(currentValue, new Step { Type = StepType.Object, Key = "", Last = true }, currentValue[""], entryValue, true);
                }
                //7.4. Otherwise, set the context's property named by the step's key to an Array containing current value and entry value, in this order. 
                else
                {
                    JsonArray arr = new JsonArray();
                    arr.Elements.Add(currentValue);
                    arr.Elements.Add(entryValue);
                    context[step.Key] = arr;
                }
                //7.5. Return context.
                return context;
            }
            //8. Otherwise, run the following substeps:
            else
            {
                //8.1. If current value is undefined, run the following subsubsteps:
                if (currentValue == null)
                {
                    //8.1.1. If step's next type is "array", set the context's property named by the step's key to a new empty Array and return it. 
                    if (step.NextType == StepType.Array)
                    {
                        return context[step.Key] = new JsonArray();
                    }
                    //8.2.2. Otherwise,set the context's property named by the step's key to a new empty Object and return it. 
                    else
                    {
                        return context[step.Key] = new JsonObject();
                    }
                }
                //8.2. Else if current value is an Object, then return the value of the context's property named by the step's key. 
                else if (currentValue is JsonObject)
                {
                    return context[step.Key];
                }
                //8.3. Else if current value is an Array, then run the following subsubsteps:
                else if (currentValue is JsonArray)
                {
                    //8.3.1. If step's next type is "array", return current value.
                    if (step.NextType == StepType.Array)
                    {
                        return currentValue;
                    }
                    //8.3.2. Otherwise, run the following subsubsubsteps:
                    else
                    {
                        //8.3.2.1. Let object be a new empty Object.
                        var @object = new JsonObject();

                        //8.3.2.2. For each item and zero-based index i in current value, if item is not undefined then set a property of object named i to item. 
                        int i = 0;
                        foreach (var item in (currentValue as JsonArray).Elements)
                        {
                            if (item != null)
                            {
                                @object[i] = item;
                            }
                            i++;
                        }

                        //8.3.2.3. Otherwise, set the context's property named by the step's key to object. 
                        context[step.Key] = @object;

                        //8.3.2.4. Return object.
                        return @object;
                    }
                }
                //8.4. Otherwise, run the following subsubsteps:
                else
                {
                    //8.4.1. Let object be a new Object with a property named by the empty string set to current value.
                    var @object = new JsonObject();
                    @object[""] = currentValue;

                    //8.4.2. Set the context's property named by the step's key to object. 
                    context[step.Key] = @object;

                    //8.4.3. Return object.
                    return @object;
                }
            }
        }

        private static List<Step> ParseJSONPath(string path)
        {
            //1. Let path be the path we are to parse.

            //2. Let original be a copy of path.
            string original = path;

            try
            {
                //3. Let steps be an empty list of steps.
                List<Step> steps = new List<Step>();

                //4. Let first key be the result of collecting a sequence of characters that are not U + 005B LEFT SQUARE BRACKET ("[") from the path.
                StringBuilder firstKey = new StringBuilder();
                for (int i = 0; i < path.Length; i++)
                {
                    var currentChar = path[i];
                    if (currentChar != '[')
                    {
                        firstKey.Append(currentChar);
                    }
                    else
                    {
                        break;
                    }
                }

                //5. If first key is empty, jump to the step labelled failure below.
                if (firstKey.Length == 0)
                {
                    goto failure;
                }

                //6. Otherwise remove the collected characters from path and push a step onto steps with its type set to "object", its key set to the collected characters, and its last flag unset.
                path = path.Substring(firstKey.Length);
                Step lastStep = new Step();
                lastStep.Type = StepType.Object;
                lastStep.Key = firstKey.ToString();
                steps.Add(lastStep);

                //7. If the path is empty, set the last flag on the last step in steps and return steps.
                if (path.Length == 0)
                {
                    lastStep.Last = true;
                    return steps;
                }

                //8. Loop: While path is not an empty string, run these substeps: 
                while (path.Length != 0)
                {
                    //8.4. If this point in the loop is reached, jump to the step labelled failure below. 
                    if (path.Length <= 1 || path[0] != '[')
                    {
                        goto failure;
                    }

                    //8.1. If the first two characters in path are U+005B LEFT SQUARE BRACKET ("[") followed by U+005D RIGHT SQUARE BRACKET ("]"), run these subsubsteps: 
                    if (path[1] == ']')
                    {
                        //8.1.1. Set the append flag on the last step in steps.
                        lastStep.Append = true;

                        //8.1.2. Remove those two characters from path.
                        path = path.Substring(2);

                        //8.1.3. If there are characters left in path, jump to the step labelled failure below.
                        if (path.Length != 0)
                        {
                            goto failure;
                        }

                        //8.1.4. Otherwise jump to the step labelled loop above.
                        continue;
                    }

                    //8.2. If the first character in path is U+005B LEFT SQUARE BRACKET ("["), followed by one or more ASCII digits, followed by U+005D RIGHT SQUARE BRACKET ("]"), run these subsubsteps:
                    if (Char.IsDigit(path[1]))
                    {
                        //8.2.1. Remove the first character from path.
                        path = path.Substring(1);

                        //8.2.2. Collect a sequence of characters being ASCII digits, remove them from path, and let numeric key be the result of interpreting them as a base-ten integer. 
                        StringBuilder numericKey = new StringBuilder();
                        for (int i = 0; i < path.Length; i++)
                        {
                            var currentChar = path[i];
                            if (Char.IsDigit(currentChar))
                            {
                                numericKey.Append(currentChar);
                            }
                            else if (currentChar == ']')
                            {
                                break;
                            }
                            else
                            {
                                goto failure;
                            }
                        }
                        int intKey = Int32.Parse(numericKey.ToString());

                        //8.2.3. Remove the following character from path.
                        path = path.Substring(numericKey.Length + 1);

                        //8.2.4. Push a step onto steps with its type set to "array", its key set to the numeric key, and its last flag unset. 
                        lastStep = new Step();
                        lastStep.Type = StepType.Array;
                        lastStep.Key = intKey;
                        steps.Add(lastStep);

                        //8.2.5. Jump to the step labelled loop above.
                        continue;
                    }

                    //8.3. If the first character in path is U+005B LEFT SQUARE BRACKET ("["), followed by one or more characters that are not U+005D RIGHT SQUARE BRACKET, followed by U+005D RIGHT SQUARE BRACKET ("]"), run these subsubsteps:
                    if (path[1] != ']')
                    {
                        //8.3.1. Remove the first character from path.
                        path = path.Substring(1);

                        //8.3.2. Collect a sequence of characters that are not U+005D RIGHT SQUARE BRACKET, remove them from path, and let object key be the result. 
                        StringBuilder objectKey = new StringBuilder();
                        for (int i = 0; i < path.Length; i++)
                        {
                            var currentChar = path[i];
                            if (currentChar != ']')
                            {
                                objectKey.Append(currentChar);
                            }
                            else
                            {
                                break;
                            }
                        }

                        //8.3.3. Remove the following character from path.
                        path = path.Substring(objectKey.Length + 1);

                        //8.3.4. Push a step onto steps with its type set to "object", its key set to the object key, and its last flag unset. 
                        lastStep = new Step();
                        lastStep.Type = StepType.Object;
                        lastStep.Key = objectKey.ToString();
                        steps.Add(lastStep);

                        //8.3.5. Jump to the step labelled loop above.
                        continue;
                    }
                }

                //9. For each step in steps, run the following substeps:
                for (int i = 0; i < steps.Count; i++)
                {
                    //9.1. If the step is the last step, set its last flag.
                    if (i == steps.Count - 1)
                    {
                        steps[i].Last = true;
                    }
                    //9.2. Otherwise, set its next type to the type of the next step in steps.
                    else
                    {
                        steps[i].NextType = steps[i + 1].Type;
                    }
                }

                //10. Return steps.
                return steps;
            }
            catch { }

            //11. Failure: return a list of steps containing a single step with its type set to "object", its key set to original, and its last flag set.
            failure:
            return new List<Step> { new Step { Key = original, Last = true, Type = StepType.Object } };
        }
    }
    
    internal class Step
    {
        public bool Append { get; internal set; }
        public object Key { get; internal set; }
        public bool Last { get; internal set; }
        public StepType NextType { get; internal set; }
        public StepType Type { get; internal set; }

        public override string ToString()
        {
            return String.Format("{0} [{1}]", Key, Type);
        }
    }

    enum StepType
    {
        Object,
        Array
    }
}
